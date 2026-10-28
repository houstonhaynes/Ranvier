export const meta = {
  name: 'ralph-issue',
  description: 'One ralph iteration in the integration worktree: plan, implement, review, gate, push and open a stacked PR for one issue',
  whenToUse: 'Called by the ralph loop with args {issue, kind, date}; kind is implement or research',
  phases: [
    { title: 'Plan', detail: 'switch to the issue branch on top of the stack, write or resume a plan' },
    { title: 'Implement', detail: 'TDD in the integration worktree, commit per step' },
    { title: 'Review', detail: 'standards and spec reviewers, each trying to refute' },
    { title: 'Gate', detail: 'build, format and the unfiltered test suite' },
    { title: 'Integrate', detail: 'record in PRIORITIES.md, push, open or update the stacked PR' },
  ],
}

const ISSUE = args.issue
const KIND = args.kind || 'implement'
const DATE = args.date
const WT = 'C:/Users/shaya/RiderProjects/Ranvier-integration'
const BRANCH = `issue/${ISSUE}`

const COMMON = `
Repository: ranvier-org/Ranvier (F#, .NET 10, Fable). All work happens in the integration worktree ${WT}; create no other worktree and never touch C:/Users/shaya/RiderProjects/Ranvier (the user works there concurrently).
Branches form a stack: integration/issues (base: master), then one issue/<n> branch per issue, each cut from the previous branch's tip. The Stack table in ${WT}/docs/.ai/PRIORITIES.md records each branch, its PR and its base.
Push and open PRs only as the Integrate step says; never comment on or edit GitHub issues; never force-push.
Prefix shell commands with rtk. Follow .claude/rules/*.md and AGENTS.md (comments.md, view-uses-core.md, framework-comparisons.md). Use the sagefs REPL as the inner loop where AGENTS.md allows it; the build CLI (dotnet fsi build.fsx -- ...) is the gate.
Commit messages: conventional commits referencing #${ISSUE}, ending with the line "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>".`

const PLAN_SCHEMA = {
  type: 'object',
  properties: {
    base: { type: 'string', description: 'the branch issue/<n> was cut from' },
    planPath: { type: 'string' },
    steps: { type: 'array', items: { type: 'string' } },
    nextSteps: { type: 'array', items: { type: 'string' }, description: 'the steps this iteration will do; at most 4' },
    blocked: { type: 'string', description: 'empty unless the issue needs a user decision before any work' },
  },
  required: ['base', 'planPath', 'steps', 'nextSteps', 'blocked'],
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

const SWITCH = `In ${WT}: if branch ${BRANCH} exists, git switch ${BRANCH}. Otherwise git switch -c ${BRANCH} from the tip of the stack (the last branch in the Stack table, or integration/issues if the table is empty), and record that branch as the base. Uncommitted changes on ${BRANCH} are work from an interrupted iteration: keep them, and finish or commit them. If git stash list has an entry named "ralph #${ISSUE} wip", git stash pop it after switching.`

const pushAndPr = (status, base) => `Stacked PR for ${BRANCH} (base ${base}):
- git push -u origin ${BRANCH}.
- If gh pr list --head ${BRANCH} --state open shows no PR: gh pr create --head ${BRANCH} --base ${base} ${status === 'done' ? '' : '--draft '}--title "<conventional title for #${ISSUE}>" with a body: summary, gate commands and test counts, "${status === 'done' ? 'Closes' : 'Part of'} #${ISSUE}", "Stacked on ${base}" when the base is not master, and ending with the line "🤖 Generated with [Claude Code](https://claude.com/claude-code)". If the PR exists, the push updates it${status === 'done' ? '; if it is a draft, gh pr ready it' : ''}.
- Add or update the row "| ${BRANCH} | #${ISSUE} | <PR url> | ${base} |" in the Stack table, commit "docs(ai): record stacked PR for #${ISSUE}", and push again.
Return the PR url.`

// ---------- research issues: one note on the issue branch ----------
if (KIND === 'research') {
  phase('Plan')
  const note = await agent(`${COMMON}
${SWITCH}
Issue #${ISSUE} is exploratory. Read it (gh issue view ${ISSUE} --comments), related issues it names, and the code and docs/.ai notes it touches.
Write docs/.ai/research/${DATE}-issue-${ISSUE}.md: the question, prior art with primary sources, how it fits the current core (cite files), options with costs, a recommended scope with concrete acceptance criteria an implementer could follow, and open questions for the user. Commit it on ${BRANCH}.
Return the base branch, the path, the commit hash and a two-line summary.`, { label: `research #${ISSUE}`, phase: 'Plan' })
  phase('Integrate')
  const pr = await agent(`${COMMON}
In ${WT} on ${BRANCH}: set the #${ISSUE} row in docs/.ai/PRIORITIES.md to done, append the log line "${DATE} #${ISSUE} research note written — <path> — <commit> — next: user decides scope", and commit "docs(ai): record ralph iteration for #${ISSUE}".
Then: ${pushAndPr('done', '<the base branch named in the research result>')}
Research result: ${note}`, { label: `integrate #${ISSUE}`, phase: 'Integrate', effort: 'low' })
  return { issue: ISSUE, kind: KIND, note, pr }
}

// ---------- implementation issues ----------
phase('Plan')
const plan = await agent(`${COMMON}
Prepare issue #${ISSUE}.
1. ${SWITCH}
2. Read the issue with gh issue view ${ISSUE} --comments, the issues it depends on, the PRIORITIES.md log lines for #${ISSUE}, and the code it touches.
3. If docs/superpowers/plans/*issue-${ISSUE}*.md exists, resume it: mark completed steps from git log. Otherwise write docs/superpowers/plans/${DATE}-issue-${ISSUE}.md: goal, acceptance criteria copied from the issue, design (view work opens with the "Core and view state" section from view-uses-core.md), and numbered TDD steps small enough for one commit each. Commit the plan on ${BRANCH}.
4. Pick at most 4 next steps for this iteration.
Set blocked only when the issue needs a decision the user must make (a public API shape the issue leaves open is NOT blocking: choose the one matching existing Ranvier conventions and record why in the plan).`, { label: `plan #${ISSUE}`, phase: 'Plan', schema: PLAN_SCHEMA })

if (!plan || plan.blocked) {
  phase('Integrate')
  await agent(`${COMMON}
In ${WT} on ${BRANCH}: set the #${ISSUE} row in docs/.ai/PRIORITIES.md to blocked, append the log line "${DATE} #${ISSUE} blocked — ${plan ? plan.blocked.replace(/`/g, "'") : 'planning agent failed'}", and commit "docs(ai): record ralph iteration for #${ISSUE}". Do not push.`, { label: 'record blocked', phase: 'Integrate', effort: 'low' })
  return { issue: ISSUE, blocked: plan ? plan.blocked : 'plan failed' }
}

phase('Implement')
let impl = await agent(`${COMMON}
Work in ${WT} on ${BRANCH}. Plan: ${plan.planPath}.
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
Review the diff ${plan.base}...${BRANCH} in ${WT} against ${scope}.
Report only defects you have confirmed by reading the code or running it; try to refute each candidate before reporting. Read-only: change no files and switch no branches.`, { label: `review:${name} #${ISSUE}`, phase: 'Review', schema: REVIEW_SCHEMA })))
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
In ${WT} on ${BRANCH}, run: dotnet fsi build.fsx -- format --dry-format, then dotnet build Ranvier.slnx, then dotnet fsi build.fsx -- test (unfiltered). If the change touches Fable-compiled sources, also dotnet fsi build.fsx -- test-fable.
Acquire the sagefs test-suite / full-build lease first if the sagefs MCP tools are available, and release it after. Change no files except a formatter fix, which you commit as "style: format #${ISSUE}".
Report passed=true only if every command exited 0, and give the test counts.`, { label: `gate #${ISSUE}`, phase: 'Gate', schema: GATE_SCHEMA }) : null

phase('Integrate')
const green = !!(gate && gate.passed && !findings.length)
const status = !impl ? 'in-progress' : impl.blocked ? 'blocked' : green && impl.done ? 'done' : 'in-progress'
const record = await agent(`${COMMON}
Record ralph iteration for #${ISSUE} in ${WT} on ${BRANCH}.
1. Set the #${ISSUE} row status in docs/.ai/PRIORITIES.md to ${status} and append one log line: "${DATE} #${ISSUE} ${status} — <one-clause outcome> — <last commit> — next: <first remaining step or blocker>". Commit "docs(ai): record ralph iteration for #${ISSUE}".
2. ${green ? pushAndPr(status, plan.base) : `Do not push: ${!gate ? 'implementation did not reach the gate' : !gate.passed ? 'gate failed: ' + gate.failures.join('; ') : 'unresolved review findings'}.`}
Inputs: impl=${JSON.stringify(impl)} gate=${JSON.stringify(gate)} findings=${JSON.stringify(findings)}`, { label: `integrate #${ISSUE}`, phase: 'Integrate', effort: 'low' })

return { issue: ISSUE, status, pushed: green, impl, gate, findings, record }
