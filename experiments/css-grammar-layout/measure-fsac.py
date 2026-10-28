"""Measure an owned FSAC LSP process without building the experiment.

The default input is Generated.fs beside this script. --smoke checks the harness
with a tiny equivalent completion surface. Every run snapshots its inputs into a
new temporary directory, restores an isolated project, and retains a JSON report.
"""
from __future__ import annotations

import argparse
from collections import deque
import hashlib
import json
import os
from pathlib import Path
import queue
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from urllib.parse import unquote
from xml.sax.saxutils import escape

try:
    import psutil
except ImportError:
    psutil = None


SETTINGS = {
    "automaticWorkspaceInit": False,
    "enableAnalyzers": False,
    "keywordsAutocomplete": False,
    "externalAutocomplete": False,
    "resolveNamespaces": False,
}


def document_key(uri):
    decoded = unquote(uri)
    return decoded.casefold() if os.name == "nt" else decoded


def terminate_owned(process):
    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(
            ["rtk", "proxy", "taskkill.exe", "/PID", str(process.pid), "/T", "/F"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
    else:
        process.kill()
    process.wait(timeout=3)


def restore_project(project, workspace, timeout):
    command = ["rtk", "dotnet", "restore", str(project), "--nologo", "--ignore-failed-sources"]
    process = subprocess.Popen(
        command, cwd=workspace, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
    )
    try:
        stdout, stderr = process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        terminate_owned(process)
        raise
    return subprocess.CompletedProcess(command, process.returncode, stdout, stderr)


class LspClient:
    def __init__(self, workspace: Path, timeout: float):
        self.workspace = workspace
        self.started = time.monotonic()
        self.deadline = self.started + timeout
        self.messages = queue.Queue()
        self.stderr = deque(maxlen=80)
        self.stray_stdout = deque(maxlen=20)
        self.notifications = deque(maxlen=80)
        self.diagnostics = {}
        self.diagnostic_times = {}
        self.analysis_versions = {}
        self.responses = {}
        self.memory = {"available": psutil is not None}
        self.memory_stop = threading.Event()
        self.next_id = 0
        self.command = [
            "rtk", "proxy", "fsautocomplete",
            "--state-directory", str(workspace / ".fsac"),
            "--log-level", "Warning",
        ]
        flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        self.process = subprocess.Popen(
            self.command, cwd=workspace, stdin=subprocess.PIPE,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=flags,
        )
        threading.Thread(target=self._read_stdout, daemon=True).start()
        threading.Thread(target=self._read_stderr, daemon=True).start()
        self.memory_thread = threading.Thread(target=self._sample_memory, daemon=True)
        self.memory_thread.start()

    def elapsed(self):
        return round(time.monotonic() - self.started, 4)

    def _read_stderr(self):
        for line in iter(self.process.stderr.readline, b""):
            self.stderr.append(line.decode("utf-8", "replace").rstrip())

    def _sample_memory(self):
        if psutil is None:
            return
        root = psutil.Process(self.process.pid)
        server = None
        while not self.memory_stop.is_set():
            try:
                if server is None:
                    server = next((
                        child for child in root.children(recursive=True)
                        if "fsautocomplete" in child.name().casefold()
                    ), None)
                if server is not None:
                    sample = server.memory_info()
                    self.memory = {
                        "available": True,
                        "server_pid": server.pid,
                        "last_rss_bytes": sample.rss,
                        "peak_working_set_bytes": max(
                            self.memory.get("peak_working_set_bytes", 0),
                            getattr(sample, "peak_wset", sample.rss),
                        ),
                        "peak_private_bytes": max(
                            self.memory.get("peak_private_bytes", 0),
                            getattr(sample, "private", 0),
                        ),
                    }
            except psutil.Error:
                break
            self.memory_stop.wait(0.2)

    def _read_stdout(self):
        try:
            while True:
                line = self.process.stdout.readline()
                if not line:
                    self.messages.put({"method": "__eof"})
                    return
                if not line.lower().startswith(b"content-length:"):
                    if line.strip():
                        self.stray_stdout.append(line.decode("utf-8", "replace").rstrip())
                    continue
                length = int(line.split(b":", 1)[1].strip())
                while True:
                    header = self.process.stdout.readline()
                    if header in (b"\r\n", b"\n"):
                        break
                    if not header:
                        raise EOFError("FSAC stopped inside a message header")
                remaining = length
                chunks = []
                while remaining:
                    chunk = self.process.stdout.read(remaining)
                    if not chunk:
                        raise EOFError("FSAC stopped inside a message body")
                    chunks.append(chunk)
                    remaining -= len(chunk)
                self.messages.put(json.loads(b"".join(chunks)))
        except Exception as error:
            self.messages.put({"method": "__reader_error", "error": repr(error)})

    def send(self, message):
        body = json.dumps(message, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        self.process.stdin.write(f"Content-Length: {len(body)}\r\n\r\n".encode("ascii") + body)
        self.process.stdin.flush()

    def notify(self, method, params):
        self.send({"jsonrpc": "2.0", "method": method, "params": params})

    def begin_request(self, method, params):
        self.next_id += 1
        self.send({"jsonrpc": "2.0", "id": self.next_id, "method": method, "params": params})
        return self.next_id

    def pump(self, deadline=None):
        end = min(self.deadline, deadline) if deadline else self.deadline
        remaining = end - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("FSAC measurement deadline exceeded")
        try:
            message = self.messages.get(timeout=remaining)
        except queue.Empty as error:
            raise TimeoutError("FSAC measurement deadline exceeded") from error
        method = message.get("method")
        if method in ("__eof", "__reader_error"):
            raise RuntimeError(f"FSAC transport ended: {message}; stderr: {list(self.stderr)}")
        if method and "id" in message:
            params = message.get("params") or {}
            if method == "workspace/configuration":
                result = [
                    SETTINGS if item.get("section", "").lower() in ("fsharp", "") else None
                    for item in params.get("items", [])
                ]
            elif method == "workspace/workspaceFolders":
                result = [{"uri": self.workspace.as_uri(), "name": self.workspace.name}]
            elif method == "workspace/applyEdit":
                result = {"applied": False, "failureReason": "Read-only measurement client"}
            else:
                result = None
            self.send({"jsonrpc": "2.0", "id": message["id"], "result": result})
        elif method:
            params = message.get("params") or {}
            if method == "textDocument/publishDiagnostics":
                uri = document_key(params.get("uri", ""))
                self.diagnostics[uri] = params.get("diagnostics", [])
                self.diagnostic_times.setdefault(uri, self.elapsed())
            elif method == "fsharp/documentAnalyzed":
                document = params.get("textDocument", {})
                self.analysis_versions[document_key(document.get("uri", ""))] = document.get("version", 0)
            self.notifications.append({
                "seconds": self.elapsed(), "method": method,
                "params": params if method == "fsharp/notifyWorkspace" else str(params)[:600],
            })
        elif "id" in message:
            self.responses[message["id"]] = message
        return message

    def await_response(self, request_id, timeout=None):
        deadline = min(self.deadline, time.monotonic() + timeout) if timeout else self.deadline
        while request_id not in self.responses:
            self.pump(deadline)
        response = self.responses.pop(request_id)
        if "error" in response:
            raise RuntimeError(f"LSP request {request_id} failed: {response['error']}")
        return response.get("result")

    def request(self, method, params, timeout=None):
        return self.await_response(self.begin_request(method, params), timeout)

    def close(self):
        self.memory_stop.set()
        self.memory_thread.join(timeout=1)
        if self.process.poll() is not None:
            return
        try:
            request_id = self.begin_request("shutdown", None)
            end = time.monotonic() + 3
            while request_id not in self.responses and time.monotonic() < end:
                self.pump(end)
            self.notify("exit", None)
            self.process.wait(timeout=3)
        except Exception:
            try:
                terminate_owned(self.process)
            except subprocess.TimeoutExpired:
                pass


def snapshot(path: Path, destination: Path):
    data = path.read_bytes()
    destination.write_bytes(data)
    return {"path": str(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def run(args):
    repo = Path(__file__).resolve().parents[2]
    if args.project:
        project = args.project.resolve()
        if project.parent.exists():
            raise ValueError("--project requires a fresh parent directory owned by this measurement")
        project.parent.mkdir(parents=True)
        workspace = project.parent
    else:
        workspace = Path(tempfile.mkdtemp(prefix="ranvier-fsac-"))
        project = workspace / "FsacMeasurement.fsproj"
    report = {
        "workspace": str(workspace), "smoke": args.smoke,
        "fsautocomplete": shutil.which("fsautocomplete"),
        "mcp_configuration_present": (repo / ".mcp.json").exists(),
        "status": "starting",
    }
    started = time.monotonic()
    client = None
    try:
        if not report["fsautocomplete"] or not shutil.which("rtk"):
            raise RuntimeError("Pinned fsautocomplete and rtk must already be installed")
        source = args.generated.resolve()
        if args.smoke:
            (workspace / "Generated.fs").write_text(
                "namespace Ranvier.Css.Compact\n"
                "module CssValue =\n"
                "    let px (value: float) = value\n"
                "    module Display =\n"
                "        let block = \"block\"\n"
                "        let none = \"none\"\n"
                "module Css =\n"
                "    let width (value: float) = string value\n",
                encoding="utf-8",
            )
            report["generated"] = {"fixture": "small completion surface"}
        else:
            report["generated"] = snapshot(source, workspace / "Generated.fs")
        report["kernel"] = snapshot(
            repo / "src/Ranvier.Css/GrammarRuntime.fs", workspace / "GrammarRuntime.fs"
        )
        runtime_dir = repo / "src/Ranvier.Css/bin/Debug/net10.0"
        runtime = runtime_dir / "Ranvier.Css.dll"
        report["runtime"] = snapshot(runtime, workspace / runtime.name)
        for dependency in runtime_dir.glob("*.dll"):
            if dependency != runtime:
                shutil.copy2(dependency, workspace / dependency.name)
        if (repo / "global.json").exists():
            shutil.copy2(repo / "global.json", workspace / "global.json")

        namespace = "Ranvier.Css.Compact" if args.smoke else args.namespace
        if not namespace:
            namespaces = re.findall(r"^namespace\s+([A-Za-z0-9_`.]+)\s*$", (workspace / "Generated.fs").read_text(encoding="utf-8"), re.MULTILINE)
            if not namespaces:
                raise ValueError("Generated source has no namespace; pass --namespace explicitly")
            namespace = namespaces[-1].replace("``", "")
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*", namespace):
            raise ValueError("Expected a dot-separated authoring namespace")
        report["authoring_namespace"] = namespace
        valid_source = (
            "module Consumer\n"
            f"open {namespace}\n"
            "let declaration () = Css.width(CssValue.px 42.)\n"
        )
        consumer = workspace / "Consumer.fs"
        consumer.write_text(valid_source, encoding="utf-8")
        references = "".join(
            f'<Reference Include="{escape(path.stem)}"><HintPath>{escape(str(path))}</HintPath></Reference>'
            for path in sorted(workspace.glob("*.dll"))
        )
        project.write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            '<TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
            '<EnableNETAnalyzers>false</EnableNETAnalyzers><NuGetAudit>false</NuGetAudit>'
            '</PropertyGroup><ItemGroup>'
            + references
            + '<Compile Include="GrammarRuntime.fs"/><Compile Include="Generated.fs"/>'
            '<Compile Include="Consumer.fs"/></ItemGroup></Project>',
            encoding="utf-8",
        )
        restore_started = time.monotonic()
        restored = restore_project(project, workspace, args.timeout)
        report["restore_seconds"] = round(time.monotonic() - restore_started, 4)
        if restored.returncode:
            raise RuntimeError(f"Restore failed ({restored.returncode}): {restored.stdout}\n{restored.stderr}")
        print(json.dumps({"phase": "restored", "workspace": str(workspace)}), flush=True)

        client = LspClient(workspace, args.timeout)
        capabilities = client.request("initialize", {
            "processId": os.getpid(),
            "rootUri": workspace.as_uri(),
            "workspaceFolders": [{"uri": workspace.as_uri(), "name": workspace.name}],
            "capabilities": {
                "workspace": {"configuration": True, "workspaceFolders": True},
                "textDocument": {
                    "completion": {"completionItem": {"snippetSupport": False}},
                    "publishDiagnostics": {"relatedInformation": True, "versionSupport": True},
                },
            },
            "initializationOptions": {"AutomaticWorkspaceInit": False},
            "clientInfo": {"name": "Ranvier FSAC measurement", "version": "1"},
        })
        report["initialize_seconds"] = client.elapsed()
        report["server_info"] = capabilities.get("serverInfo")
        client.notify("initialized", {})
        client.notify("workspace/didChangeConfiguration", {"settings": {"FSharp": SETTINGS}})
        load_start = time.monotonic()
        load_request = client.begin_request("fsharp/workspaceLoad", {
            "textDocuments": [{"uri": project.as_uri()}],
        })
        client.notify("textDocument/didOpen", {
            "textDocument": {
                "uri": consumer.as_uri(), "languageId": "fsharp",
                "version": 1, "text": valid_source,
            },
        })
        report["workspace_load_response"] = client.await_response(load_request)
        consumer_key = document_key(consumer.as_uri())
        while client.analysis_versions.get(consumer_key, 0) < 1:
            client.pump()
        report["load_to_analysis_seconds"] = round(time.monotonic() - load_start, 4)
        report["cold_open_to_analysis_seconds"] = client.elapsed()
        report["diagnostics_notification_received"] = consumer_key in client.diagnostics
        report["consumer_diagnostics"] = client.diagnostics.get(consumer_key, [])
        report["all_diagnostics"] = {
            uri: diagnostics[:30] for uri, diagnostics in client.diagnostics.items() if diagnostics
        }
        print(json.dumps({
            "phase": "consumer_analyzed",
            "seconds": report["cold_open_to_analysis_seconds"],
            "count": len(report["consumer_diagnostics"]),
        }), flush=True)

        incomplete = valid_source + "let discover = CssValue.Display.\n"
        edit_start = time.monotonic()
        client.notify("textDocument/didChange", {
            "textDocument": {"uri": consumer.as_uri(), "version": 2},
            "contentChanges": [{"text": incomplete}],
        })
        while client.analysis_versions.get(consumer_key, 0) < 2:
            client.pump()
        completion_start = time.monotonic()
        completion = client.request("textDocument/completion", {
            "textDocument": {"uri": consumer.as_uri()},
            "position": {"line": 3, "character": len("let discover = CssValue.Display.")},
            "context": {"triggerKind": 2, "triggerCharacter": "."},
        })
        report["completion_seconds"] = round(time.monotonic() - completion_start, 4)
        report["edit_to_completion_seconds"] = round(time.monotonic() - edit_start, 4)
        items = completion.get("items", []) if isinstance(completion, dict) else completion or []
        report["completion_labels"] = [item.get("label", "") for item in items]
        report["completion_count"] = len(items)
        report["expected_labels_present"] = {
            name: name in report["completion_labels"] for name in ("block", "none")
        }
        report["workspace_events"] = [
            event for event in client.notifications if event["method"] == "fsharp/notifyWorkspace"
        ]
        report["status"] = (
            "passed" if all(report["expected_labels_present"].values())
            and not any(d.get("severity") == 1 for d in report["consumer_diagnostics"])
            else "diagnostics_or_missing_completions"
        )
    except Exception as error:
        report["status"] = "failed"
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        if client:
            report["fsac_elapsed_seconds"] = client.elapsed()
            report["fsac_command"] = client.command
            report["stderr_tail"] = list(client.stderr)
            report["stray_stdout"] = list(client.stray_stdout)
            report["notifications"] = list(client.notifications)
            client.close()
            report["owned_process_exit_code"] = client.process.poll()
            report["memory"] = client.memory
        report["total_seconds"] = round(time.monotonic() - started, 4)
        output = args.output.resolve() if args.output else workspace / "report.json"
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
        print(json.dumps({
            key: value for key, value in report.items()
            if key not in ("notifications", "workspace_events", "all_diagnostics", "consumer_diagnostics")
        }, indent=2, ensure_ascii=False))
        print(f"Report: {output}", flush=True)
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--generated", type=Path, default=Path(__file__).with_name("Generated.fs"))
    parser.add_argument("--namespace", help="Authoring namespace; defaults to the last namespace in the generated source")
    parser.add_argument("--project", type=Path, help="Create the isolated project here; its parent directory must be new")
    parser.add_argument("--timeout", type=float, default=180, help="FSAC deadline in seconds")
    parser.add_argument("--output", type=Path, help="Optional JSON report destination")
    parser.add_argument("--smoke", action="store_true", help="Validate the transport against a tiny source fixture")
    sys.exit(run(parser.parse_args()))
