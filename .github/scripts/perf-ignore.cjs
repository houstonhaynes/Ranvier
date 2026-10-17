// The reason of a `Perf-Ignore: <reason>` trailer in the footer (the last paragraph) of a commit message, or null.
// The Perf workflows skip measuring and recording a master commit carrying this trailer.
function ignoreReason(message) {
  const paragraphs = (message ?? '').replace(/\r\n?/g, '\n').trim().split(/\n[ \t]*\n/);
  if (paragraphs.length < 2) return null;
  for (const line of paragraphs.at(-1).split('\n')) {
    const match = /^Perf-Ignore:(.*)$/i.exec(line.trim());
    if (!match) continue;
    const reason = match[1].trim();
    if (!reason) throw new Error('Perf-Ignore trailer requires a reason, e.g. "Perf-Ignore: docs only".');
    return reason;
  }
  return null;
}

module.exports = { ignoreReason };
