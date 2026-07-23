const fs = require('fs');
const path = String.raw`C:\Users\zasah\.cursor\projects\c-Users-zasah-Documents-MOMO-Quant\agent-transcripts\a494dec4-5d64-483d-8bff-a68d93457d4a\subagents\2f4006c2-c830-43b5-88a3-606efc972bfd.jsonl`;
const line = fs.readFileSync(path, 'utf8').split('\n')[0];
const text = JSON.parse(line).message.content[0].text;
const keys = ['ACCEPTANCE REPORT', 'Acceptance items', '### ACCEPTANCE', 'Return full acceptance', 'ACCEPTANCE'];
for (const k of keys) console.log(k, text.indexOf(k));
console.log('---TAIL 15000---');
console.log(text.slice(-15000));
