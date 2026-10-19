const types = ['build', 'bench', 'chore', 'ci', 'docs', 'feat', 'fix', 'perf', 'refactor', 'revert', 'style', 'test'];
const header = new RegExp(`^(${types.join('|')})(\\([a-z0-9][a-z0-9._/-]*\\))?!?: \\S.*$`);

// The problem with a commit subject or pull-request title, or null when it is a conventional commit header.
function headerProblem(subject) {
  const line = (subject ?? '').split(/\r?\n/)[0];
  if (header.test(line)) return null;
  if (!line.trim()) return 'is empty';
  return `"${line}" is not "type(scope): summary" with a type from ${types.join(', ')}`;
}

async function check({ context, core }) {
  const pr = context.payload.pull_request;
  if (pr.user.type === 'Bot') {
    core.notice(`${pr.user.login} is a bot; the title is not checked.`);
    return;
  }
  const p = headerProblem(pr.title);
  if (p) core.setFailed(`Title ${p}. See CONTRIBUTING.md#commit-messages.`);
}

module.exports = { types, headerProblem, check };
