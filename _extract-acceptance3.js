const fs = require('fs');
const path = 'C:/Users/zasah/.cursor/projects/c-Users-zasah-Documents-MOMO-Quant/agent-transcripts/a494dec4-5d64-483d-8bff-a68d93457d4a/a494dec4-5d64-483d-8bff-a68d93457d4a.jsonl';
const lines = fs.readFileSync(path, 'utf8').split('\n').filter(Boolean);
const text = JSON.parse(lines[3760]).message.content[0].text;
fs.writeFileSync('C:/Users/zasah/Documents/MOMO Quant/_m222-full-spec.txt', text);
console.log('len', text.length);
const markers = ['ACCEPTANCE', 'Acceptance Criteria', 'Part 26', '1. Build', '33.', 'Manual Verification'];
for (const m of markers) console.log(m, text.indexOf(m));
let best = -1;
let bestCount = 0;
for (const m of text.matchAll(/\n1\.\s+/g)) {
  const slice = text.slice(m.index, m.index + 15000);
  const count = (slice.match(/\n\d+\.\s+/g) || []).length;
  if (count > bestCount) {
    bestCount = count;
    best = m.index;
  }
}
console.log('best', best, 'count', bestCount);
if (best >= 0) {
  fs.writeFileSync('C:/Users/zasah/Documents/MOMO Quant/_acceptance-section.txt', text.slice(best, best + 14000));
}
