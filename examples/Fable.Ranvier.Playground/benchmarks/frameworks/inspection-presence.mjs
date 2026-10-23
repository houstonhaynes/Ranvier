export function inspectionFailures(text, expectPresent = false) {
  const region = /\/\/#region [^\n]*src\/Ranvier\.View\.Dom\/(Inspect|Debug)\.js/;
  const probe = /\b(ListProbe|ArmProbe|BoundaryProbe|HoleProbe|InstanceProbe|MountProbe)\b/;
  const failures = [];
  if (region.test(text) !== expectPresent)
    failures.push(expectPresent ? "positive control is missing an Inspect or Debug module" : "the bundle holds an Inspect or Debug module");
  if (probe.test(text) !== expectPresent)
    failures.push(expectPresent ? "positive control is missing a probe case" : "the bundle names a probe case");
  return failures;
}
