// Renders a plain-text transcript to a terminal-style PNG with Chromium.
// usage: NODE_PATH=<dir with playwright> node render-terminal.cjs <in.txt> <out.png> <title>
const { chromium } = require('playwright');
const fs = require('fs');

const [input, output, title] = process.argv.slice(2);
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const body = fs
  .readFileSync(input, 'utf8')
  .trimEnd()
  .split('\n')
  .map((l) => (l.startsWith('$') ? `<span class="p">$</span>${esc(l.slice(1))}` : esc(l)))
  .join('\n');

const html = `<!doctype html><meta charset="utf-8"><style>
*{box-sizing:border-box}body{margin:0;background:#e8ebf0;font-family:Inter,-apple-system,"Segoe UI",sans-serif}
.canvas{width:1000px;padding:28px 28px 14px}
.term{background:#0f172a;color:#e2e8f0;border-radius:10px;box-shadow:0 8px 30px rgba(0,0,0,.25);overflow:hidden;font-family:"JetBrains Mono","SF Mono",Menlo,Consolas,monospace;font-size:13px}
.tb{background:#1e293b;padding:8px 12px;color:#94a3b8;font-size:12px}
pre{margin:0;padding:16px 18px;white-space:pre-wrap;line-height:1.55}.p{color:#34d399}
.mark{font-size:11px;color:#6b7280;text-align:center;padding:10px 0 2px}
</style><div class="canvas"><div class="term"><div class="tb">${esc(title)}</div><pre>${body}</pre></div>
<div class="mark">Real output captured from a running instance in CI.</div></div>`;

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1000, height: 600 }, deviceScaleFactor: 1.5 });
  await page.setContent(html);
  await page.locator('.canvas').screenshot({ path: output });
  await browser.close();
})();
