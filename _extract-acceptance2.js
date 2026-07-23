const fs = require('fs');
const path = String.raw`C:\Users\zasah\.cursor\projects\c-Users-zasah-Documents-MOMO-Quant\agent-transcripts\a494dec4-5d64-483d-8bff-a68d93457d4a\a494dec4-5d64-483d-8bff-a68d93457d4a.jsonl`;
const lines = fs.readFileSync(path, 'utf8').split('\n').filter(Boolean);
console.log('lines', lines.length);
for (let i = 0; i < lines.length; i++) {
  const text = JSON.parse(lines[i]).message?.content?.[0]?.text || '';
  if (text.includes('acceptance items 1-33') || text.includes('Acceptance items') || text.includes('ACCEPTANCE CHECKLIST') || /33\./.test(text) && text.includes('Holdout')) {
    console.log('FOUND in line', i, 'len', text.length);
    const idx = text.indexOf('1.');
    // find section with many numbered items
    const m = text.match(/(?:ACCEPTANCE|Acceptance|Part 26|checklist)[\s\S]{0,20000}/i);
    if (m) {
      fs.writeFileSync(String.raw`C:\Users\zasah\Documents\MOMO Quant\_acceptance-section.txt`, m[0].slice(0, 20000));
      console.log('wrote section', m[0].slice(0, 500));
    }
  }
  if (text.includes('Return full acceptance items')) {
    console.log('return full at line', i);
    const idx = text.lastIndexOf('1.');
    // search for pattern like numbered list of 33
    const parts = [];
    for (let n = 1; n <= 33; n++) {
      const re = new RegExp(`(?:^|\\n)\\s*${n}\\.\\s+[^\\n]+`);
      const mm = text.match(re);
      if (mm) parts.push(mm[0].trim());
    }
    console.log('found numbered', parts.length);
    if (parts.length > 10) {
      fs.writeFileSync(String.raw`C:\Users\zasah\Documents\MOMO Quant\_acceptance-section.txt`, parts.join('\n'));
    } else {
      // dump last 20k
      fs.writeFileSync(String.raw`C:\Users\zasah\Documents\MOMO Quant\_acceptance-section.txt`, text.slice(-25000));
    }
  }
}
