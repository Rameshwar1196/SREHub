import { copyFileSync, mkdirSync } from 'node:fs';
import { build } from 'esbuild';

const target = 'wwwroot/lib';
mkdirSync(target, { recursive: true });

copyFileSync('node_modules/mammoth/mammoth.browser.min.js', `${target}/mammoth.browser.min.js`);
copyFileSync('node_modules/xlsx/dist/xlsx.full.min.js', `${target}/xlsx.full.min.js`);

await build({
    entryPoints: ['node_modules/pptx-preview/dist/pptx-preview.es.js'],
    bundle: true,
    minify: true,
    format: 'iife',
    globalName: 'PptxPreview',
    outfile: `${target}/pptx-preview.min.js`
});
