export const meta = {
  name: 'ralph-issue',
  description: 'One ralph iteration: plan, implement, review, gate and merge one GitHub issue into integration/issues',
  whenToUse: 'Called by the ralph loop with args {issue, kind, date}; kind is implement or research',
  phases: [
    { title: 'Plan', detail: 'read the issue and the code, write or resume a plan' },
    { title: 'Implement', detail: 'TDD in the issue worktree, commit per step' },
    { title: 'Review', detail: 'standards and spec reviewers, each trying to refute' },
    { title: 'Gate', detail: 'build, format and the unfiltered test suite' },
    { title: 'Integrate', detail: 'merge into integration/issues and update PRIORITIES.md' },
  ],
}

const ISSUE = args.issue
const KIND = args.kind || 'implement'
const DATE = args.date
const ROOT = 'C:/Users/shaya/RiderProjects'
const INTEGRATION = `${ROOT}/Ranvier-integration`
const WT = `${ROOT}/Ranvier-issue-${ISSUE}`
const BRANCH = `issue/${ISSUE}`

const COMMON = `
Repository: shayanhabibi/Ranvier (F#, .NET 10, Fable). Integration worktree: ${INTEGRATION} on branch integration/issues.
Priorities and loop log: ${INTEGRATION}/docs/.ai/PRIORITIES.md.
Never push, never open PRs, never comment on or edit GitHub issues. Never touch C:/Users/shaya/RiderProjects/Ranvier (the user's main checkout, which has uncommitted work).
Prefix shell commands with rtk. Follow .claude/rules/*.md and AGENTS.md in the worktree (comments.md, view-uses-core.md, framework-comparisons.md). Use the sagefs REPL as the inner loop where AGENTS.md allows it; the build CLI (dotnet fsi build.fsx -- ...) is the gate.
Commit messages: conventional commits referencing #${ISSUE}, ending with the line "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>".`

const PLAN_SCHEMA = {
  type: 'object',
  properties: {
    planPath: { type: 'string' },
    steps: { type: 'array', items: { type: 'string' } },
    nextSteps: { type: 'array', items: { type: 'string' }, description: 'the steps this iteration will do; at most 4' },
    blocked: { type: 'string', description: 'empty unless the issue needs a user decision before any work' },
  },
  required: ['planPath', 'steps', 'nextSteps', 'blocked'],
}
const IMPL_SCHEMA = {
  type: 'object',
  properties: {
    commits: { type: 'array', items: { type: 'string' } },
    done: { type: 'boolean', description: 'every acceptance criterion of the issue met' },
    summary: { type: 'string' },
    remaining: { type: 'array', items: { type: 'string' } },
    blocked: { type: 'string' },
  },
  required: ['commits', 'done', 'summary', 'remaining', 'blocked'],
}
const REVIEW_SCHEMA = {
  type: 'object',
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: { file: { type: 'string' }, line: { type: 'number' }, severity: { type: 'string', enum: ['blocker', 'major', 'minor'] }, issue: { type: 'string' } },
        required: ['file', 'severity', 'issue'],
      },
    },
  },
  required: ['findings'],
}
const GATE_SCHEMA = {
  type: 'object',
  properties: {
    passed: { type: 'boolean' },
    ran: { type: 'string', description: 'commands run and test counts' },
    failures: { type: 'array', items: { type: 'string' } },
  },
  required: ['passed', 'ran', 'failures'],
}

// ---------- research issues: one note, committed on integration/issues ----------
if (KIND === 'research') {
  phase('Plan')
  const note = await agent(`${COMMON}
Issue #${ISSUE} is exploratory. Read it (gh issue view ${ISSUE} --comments), related issues it names, and the code and docs/.ai notes it touches, working in ${INTEGRATION}.
Write docs/.ai/research/${DATE}-issue-${ISSUE}.md: the question, prior art with primary sources, how it fits the current core (cite files), options with costs, a recommended scope with concrete acceptance criteria an implementer could follow, and open questions for the user.
Commit it on integration/issues. Return the path, the commit hash and a two-line summary.`, { label: `research #${ISSUE}`, phase: 'Plan' })
  phase('Integrate')
  await agent(`${COMMON}
In ${INTEGRATION}, update the row for #${ISSUE} in docs/.ai/PRIORITIES.md to status done, and append a log line: "${DATE} #${ISSUE} research note written — <path> — <commit> — next: user decides scope". Use this result: ${note}
Commit with message "docs(ai): record ralph iteration for #${ISSUE}".`, { label: 'record', phase: 'Integrate', effort: 'low' })
  return { issue: ISSUE, kind: KIND, note }
}

// ---------- implementation issues ----------
phase('Plan')
const plan = await agent(`${COMMON}
Prepare issue #${ISSUE}.
1. If ${WT} does not exist, create it: git -C ${INTEGRATION} worktree add -b ${BRANCH} ${WT} integration/issues (if ${BRANCH} exists, add the worktree on it instead, then merge integration/issues into it).
2. Read the issue with gh issue view ${ISSUE} --comments, the issues it depends on, docs/.ai/PRIORITIES.md log lines for #${ISSUE}, and the code it touches.
3. If docs/superpowers/plans/*issue-${ISSUE}*.md exists in ${WT}, resume it: mark completed steps from git log. Otherwise write docs/superpowers/plans/${DATE}-issue-${ISSUE}.md: goal, acceptance criteria copied from the issue, design (view work opens with the "Core and view state" section from view-uses-core.md), and numbered TDD steps small enough for one commit each. Commit the plan on ${BRANCH}.
4. Pick at most 4 next steps for this iteration.
Set blocked only when the issue needs a decision the user must make (a public API shape the issue leaves open is NOT blocking: choose the one matching existing Ranvier conventions and record why in the plan).`, { label: `plan #${ISSUE}`, phase: 'Plan', schema: PLAN_SCHEMA })

if (!plan || plan.blocked) {
  phase('Integrate')
  await agent(`${COMMON}
In ${INTEGRATION}, set the #${ISSUE} row in docs/.ai/PRIORITIES.md to blocked and append the log line "${DATE} #${ISSUE} blocked — ${plan ? plan.blocked.replace(/`/g, "'") : 'planning agent failed'}". Commit "docs(ai): record ralph iteration for #${ISSUE}".`, { label: 'record blocked', phase: 'Integrate', effort: 'low' })
  return { issue: ISSUE, blocked: plan ? plan.blocked : 'plan failed' }
}

phase('Implement')
let impl = await agent(`${COMMON}
Work in ${WT} on branch ${BRANCH}. Plan: ${plan.planPath}.
Do these steps, test first (red, then green), one commit per step, ticking the step in the plan file:
${plan.nextSteps.map((s, i) => `${i + 1}. ${s}`).join('\n')}
Before each commit run the build for the touched projects and the tests that cover them. Do not weaken or delete existing tests or thresholds to make them pass.
Report done=true only if every acceptance criterion of the issue is now met.`, { label: `implement #${ISSUE}`, phase: 'Implement', schema: IMPL_SCHEMA })

let findings = []
for (let round = 0; round < 2 && impl && !impl.blocked; round++) {
  const lenses = [
    ['standards', `the repo's written standards: .claude/rules/*.md, AGENTS.md, CONTRIBUTING.md, the fsharp-xml-docs skill, Fantomas formatting, and the idiom of the surrounding code`],
    ['spec', `issue #${ISSUE} (gh issue view ${ISSUE}) and the plan ${plan.planPath}: correctness, missing acceptance criteria, untested behaviour, reactive invariants (ordering, batching, ownership, disposal), and performance regressions against the baselines in docs/.ai/benchmarks`],
  ]
  const reviews = await parallel(lenses.map(([name, scope]) => () => agent(`${COMMON}
Review the diff integration/issues...${BRANCH} in ${WT} against ${scope}.
Report only defects you have confirmed by reading the code or running it; try to refute each candidate before reporting. Read-only: change no files.`, { label: `review:${name} #${ISSUE}`, phase: 'Review', schema: REVIEW_SCHEMA })))
  findings = reviews.filter(Boolean).flatMap(r => r.findings).filter(f => f.severity !== 'minor')
  log(`review round ${round + 1}: ${findings.length} blocker/major findings`)
  if (!findings.length) break
  impl = await agent(`${COMMON}
In ${WT} on ${BRANCH}, fix these review findings, test first where behaviour changes, and commit:
${findings.map(f => `- [${f.severity}] ${f.file}${f.line ? ':' + f.line : ''} ${f.issue}`).join('\n')}
Keep the previous result's done flag unless a fix changes it. Previous result: ${JSON.stringify(impl)}`, { label: `fix #${ISSUE}`, phase: 'Implement', schema: IMPL_SCHEMA })
}

phase('Gate')
const gate = impl && !impl.blocked ? await agent(`${COMMON}
In ${WT}, run: dotnet fsi build.fsx -- format --dry-format, then dotnet build Ranvier.slnx, then dotnet fsi build.fsx -- test (unfiltered). If the change touches Fable-compiled sources, also dotnet fsi build.fsx -- test-fable.
Acquire the sagefs test-suite / full-build lease first if the sagefs MCP tools are available, and release it after. Run nothing that changes files except a formatter fix, which you commit as "style: format #${ISSUE}".
Report passed=true only if every command exited 0, and give the test counts.`, { label: `gate #${ISSUE}`, phase: 'Gate', schema: GATE_SCHEMA }) : null

phase('Integrate')
const merge = gate && gate.passed && !findings.length
const status = !impl ? 'in-progress' : impl.blocked ? 'blocked' : merge && impl.done ? 'done' : 'in-progress'
const record = await agent(`${COMMON}
Record ralph iteration for #${ISSUE}.
${merge ? `1. In ${INTEGRATION}: git merge --no-ff ${BRANCH} -m "merge: #${ISSUE} into integration/issues". If it conflicts, resolve only if trivial, otherwise abort the merge and report it.` : `1. Do not merge: ${!gate ? 'implementation did not reach the gate' : !gate.passed ? 'gate failed: ' + gate.failures.join('; ') : 'unresolved review findings'}.`}
2. In ${INTEGRATION}, set the #${ISSUE} row status in docs/.ai/PRIORITIES.md to ${status} and append one log line: "${DATE} #${ISSUE} ${status} — <one-clause outcome> — <merge or last commit> — next: <first remaining step or blocker>".
3. Commit "docs(ai): record ralph iteration for #${ISSUE}".
Inputs: impl=${JSON.stringify(impl)} gate=${JSON.stringify(gate)} findings=${JSON.stringify(findings)}`, { label: `integrate #${ISSUE}`, phase: 'Integrate', effort: 'low' })

return { issue: ISSUE, status, merged: merge, impl, gate, findings, record }
