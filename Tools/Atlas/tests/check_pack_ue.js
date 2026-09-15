#!/usr/bin/env node
/**
 * check_pack_ue.js —— pack_ue.js 的行为自检
 *
 *   node tests/check_pack_ue.js        # 在仓库根目录跑
 *
 * 钉住这几件事（都踩过）：
 *   1. 内部的「序号_」排序前缀不能泄漏到产出的 sprite 名里。
 *   2. 剔除前缀必须精确 —— 源图本来就叫 0000_a.png 也不能被误伤。
 *   3. --prepend-folder 要真的生效（曾经因为假路径里没有 / 而是静默空操作）。
 *   4. --format unreal 要同时产出官方模板版与 _array 版两份，不能互相覆盖。
 *   5. 源帧是同一张满画布时，sourceSize 必须保留原尺寸（UE 锚点一致的前提）。
 *   6. 清单接口：名字原样落盘、_sequence.json 按序号升序、重名/缺文件要报错。
 *   7. 货架式装箱在「图元尺寸接近」时明显优于 MaxRects。
 *   8. 旋转：core 只传 3 个参数给装箱器，allowRotation 决定姿态，不能被吃掉。
 */

const fs = require('fs');
const os = require('os');
const path = require('path');
const zlib = require('zlib');
const { execFileSync } = require('child_process');

const REPO = path.resolve(__dirname, '..');
const SCRIPT = path.join(REPO, 'pack_ue.js');

let pass = 0, fail = 0;
function check(name, cond, detail) {
  if (cond) { pass++; console.log('  ok   ' + name); }
  else { fail++; console.log('  FAIL ' + name + (detail ? '  -> ' + detail : '')); }
}

// ---------------------------------------------------------------- 最小 PNG 编码器
// 只为造测试图，不引三方依赖。RGBA8，单个 IDAT，走 zlib。
function crc32(buf) {
  let c, table = [];
  for (let n = 0; n < 256; n++) {
    c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  let crc = 0xFFFFFFFF;
  for (const b of buf) crc = table[(crc ^ b) & 0xFF] ^ (crc >>> 8);
  return (crc ^ 0xFFFFFFFF) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}

function writePng(file, w, h, rgba) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  const raw = Buffer.alloc(h * (1 + w * 4));
  for (let y = 0; y < h; y++) {
    raw[y * (1 + w * 4)] = 0;                                   // filter: none
    rgba.copy(raw, y * (1 + w * 4) + 1, y * w * 4, (y + 1) * w * 4);
  }
  fs.writeFileSync(file, Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]));
}

// 造一张 w×h 的满画布帧：四周一圈透明，中间画一块实心色块
function framePng(file, w, h, inset, rgb) {
  const buf = Buffer.alloc(w * h * 4, 0);
  for (let y = inset; y < h - inset; y++) {
    for (let x = inset; x < w - inset; x++) {
      const i = (y * w + x) * 4;
      buf[i] = rgb[0]; buf[i + 1] = rgb[1]; buf[i + 2] = rgb[2]; buf[i + 3] = 255;
    }
  }
  writePng(file, w, h, buf);
}

function run(args, cwd) {
  return execFileSync(process.execPath, [SCRIPT, ...args],
    { cwd: cwd || REPO, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
}

function readJson(p) { return JSON.parse(fs.readFileSync(p, 'utf8')); }

function frameNames(p) {
  const d = readJson(p);
  return Array.isArray(d.frames) ? d.frames.map(f => f.filename) : Object.keys(d.frames);
}

// ---------------------------------------------------------------- 用例

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'packue-'));
const IN = path.join(tmp, 'in');
const OUT = path.join(tmp, 'out');
fs.mkdirSync(IN, { recursive: true });

// 5 张同一画布（200x120）的帧 + 1 张故意叫 0000_ 的
for (let i = 0; i < 5; i++) {
  framePng(path.join(IN, `Misaka-Sk2-aaa${i}.png`), 200, 120, 8 + i * 4, [200, 60 + i * 30, 40]);
}
framePng(path.join(IN, '0000_trap.png'), 200, 120, 30, [30, 30, 200]);

console.log('\n[1] 排序前缀不能泄漏进 sprite 名');
run([IN, '-o', OUT, '--name', 't1', '--format', 'unreal', '--trim', '--extrude', '1', '--padding', '2']);
const EXPECT = ['0000_trap', 'Misaka-Sk2-aaa0', 'Misaka-Sk2-aaa1',
                'Misaka-Sk2-aaa2', 'Misaka-Sk2-aaa3', 'Misaka-Sk2-aaa4'];
const n1 = frameNames(path.join(OUT, 't1_array.paper2dsprites')).slice().sort();
// 注意：不能断言「名字里没有 NNNN_」—— 源图本来就叫 0000_trap.png 的话，它就该带头。
// 正确断言是「名字集合 == 源文件名（去扩展名）集合」，多一个前缀都会让它对不上。
check('名字集合 = 源文件名集合（前缀已剔净）',
  JSON.stringify(n1) === JSON.stringify(EXPECT), n1.join(', '));
check('源图 0000_trap 没被误伤（也没被二次剔除）',
  n1.filter(n => n.includes('trap')).join(',') === '0000_trap',
  '实际: ' + n1.filter(n => n.includes('trap')).join(','));
check('帧数 = 6', n1.length === 6, '实际 ' + n1.length);

console.log('\n[2] sourceSize 保留原画布尺寸（锚点一致的前提）');
{
  const d = readJson(path.join(OUT, 't1_array.paper2dsprites'));
  const ss = new Set(d.frames.map(f => `${f.sourceSize.w}x${f.sourceSize.h}`));
  check('6 帧 sourceSize 只有一种', ss.size === 1, [...ss].join(', '));
  check('且等于源画布 200x120', [...ss][0] === '200x120', [...ss].join(', '));
}

console.log('\n[3] --format unreal 要留下官方模板版 + _array 版两份');
check('t1.paper2dsprites（官方模板版）存在', fs.existsSync(path.join(OUT, 't1.paper2dsprites')));
check('t1_array.paper2dsprites（数组版）存在', fs.existsSync(path.join(OUT, 't1_array.paper2dsprites')));
{
  const off = readJson(path.join(OUT, 't1.paper2dsprites'));
  const arr = readJson(path.join(OUT, 't1_array.paper2dsprites'));
  check('两份内容不同（没有互相覆盖）',
    JSON.stringify(off) !== JSON.stringify(arr));
  check('_array 版是数组 + filename',
    Array.isArray(arr.frames) && typeof arr.frames[0].filename === 'string');
  const offNames = (Array.isArray(off.frames) ? off.frames.map(f => f.filename) : Object.keys(off.frames))
    .slice().sort();
  check('官方模板版名字也干净', JSON.stringify(offNames) === JSON.stringify(EXPECT), offNames.join(', '));
}

console.log('\n[4] --prepend-folder 要真的生效');
{
  const NEST = path.join(tmp, 'nest');
  fs.mkdirSync(path.join(NEST, 'Sk2_a'), { recursive: true });
  fs.mkdirSync(path.join(NEST, 'Sk2_b'), { recursive: true });
  framePng(path.join(NEST, 'Sk2_a', 'f1.png'), 200, 120, 10, [255, 0, 0]);
  framePng(path.join(NEST, 'Sk2_b', 'f2.png'), 200, 120, 10, [0, 255, 0]);
  const OUT2 = path.join(tmp, 'out2');
  run([NEST, '-o', OUT2, '--name', 't2', '--format', 'json-array', '--recursive', '--prepend-folder']);
  const n2 = frameNames(path.join(OUT2, 't2.paper2dsprites'));
  check('名字带上了文件夹前缀', n2.every(n => /^Sk2_[ab]\//.test(n)), n2.join(', '));

  const OUT3 = path.join(tmp, 'out3');
  run([NEST, '-o', OUT3, '--name', 't3', '--format', 'json-array', '--recursive']);
  const n3 = frameNames(path.join(OUT3, 't3.paper2dsprites'));
  check('不加 --prepend-folder 时只有文件名', n3.every(n => !n.includes('/')), n3.join(', '));
}

console.log('\n[5] 源帧尺寸不统一要给出警告');
{
  const MIX = path.join(tmp, 'mix');
  fs.mkdirSync(MIX, { recursive: true });
  framePng(path.join(MIX, 'a.png'), 100, 80, 6, [255, 0, 0]);
  framePng(path.join(MIX, 'b.png'), 160, 140, 6, [0, 0, 255]);
  const outLog = run([MIX, '-o', path.join(tmp, 'out4'), '--name', 't4', '--format', 'json-array']);
  check('提示了锚点会不一致', /锚点/.test(outLog) && /跳/.test(outLog));
}

console.log('\n[6] 清单接口：名字与序号由清单给定');
{
  const MSRC = path.join(tmp, 'man_in');
  fs.mkdirSync(MSRC, { recursive: true });
  const n = 6;
  for (let i = 0; i < n; i++) {
    framePng(path.join(MSRC, `raw_${i}.png`), 64, 80, 6 + i * 3, [30 + i * 30, 120, 200]);
  }
  // 故意让序号 ≠ 文件名顺序，名字也 ≠ 文件名
  const order = [4, 1, 6, 2, 5, 3];
  const frames = order.map((ix, i) => ({
    file: `raw_${i}.png`, name: `Misaka_Sk2_${String(ix).padStart(2, '0')}`, index: ix,
  }));
  const manPath = path.join(MSRC, 'atlas_manifest.json');
  fs.writeFileSync(manPath, JSON.stringify({
    atlas: 'Misaka_Sk2', spritePrefix: 'Misaka_Sk2',
    mode: 'pack', trim: true, padding: 2, frames,
  }, null, 2), 'utf8');

  const tsvPath = path.join(MSRC, 'atlas_manifest.tsv');
  fs.writeFileSync(tsvPath,
    '# file\tname\tindex\n' + frames.map(f => `${f.file}\t${f.name}\t${f.index}`).join('\n') + '\n',
    'utf8');

  const MOUT = path.join(tmp, 'man_out');
  const rep = path.join(MOUT, 'report.json');
  run(['--manifest', manPath, '-o', MOUT, '--format', 'unreal', '--report', rep]);

  const arr = readJson(path.join(MOUT, 'Misaka_Sk2_array.paper2dsprites'));
  const got = arr.frames.map(f => f.filename).slice().sort();
  const want = frames.map(f => f.name).slice().sort();
  check('精灵名原样落盘（与文件名无关）', JSON.stringify(got) === JSON.stringify(want), got.join(', '));

  const seq = readJson(path.join(MOUT, 'Misaka_Sk2_sequence.json'));
  const idx = seq.frames.map(f => f.index);
  check('_sequence.json 按 index 严格升序',
    JSON.stringify(idx) === JSON.stringify(idx.slice().sort((a, b) => a - b)), idx.join(','));
  check('序号就是清单给的那组',
    JSON.stringify(idx.slice().sort((a, b) => a - b)) ===
    JSON.stringify(order.slice().sort((a, b) => a - b)), idx.join(','));
  check('序号顺序与 name 对得上',
    JSON.stringify(seq.frames.map(f => f.name)) ===
    JSON.stringify(order.slice().sort((a, b) => a - b).map(i => `Misaka_Sk2_${String(i).padStart(2, '0')}`)),
    seq.frames.map(f => f.name).join(','));
  check('每帧都带图集矩形 / sourceSize',
    seq.frames.every(f => f.frame && f.sourceSize && f.spriteSourceSize));

  const rj = readJson(rep);
  check('--report 字段齐',
    ['ok', 'atlas', 'image', 'sequence', 'size', 'frameCount'].every(k => k in rj),
    Object.keys(rj).join(','));
  check('--report frameCount 对', rj.frameCount === n, String(rj.frameCount));

  // TSV 与 JSON 等价
  const MOUT2 = path.join(tmp, 'man_out2');
  run(['--manifest', tsvPath, '-o', MOUT2, '--name', 'Misaka_Sk2', '--format', 'json-array', '--trim']);
  const a2 = readJson(path.join(MOUT2, 'Misaka_Sk2.paper2dsprites'));
  check('JSON 与 TSV 两种清单结果一致',
    JSON.stringify(a2.frames.map(f => f.filename).slice().sort()) === JSON.stringify(got));

  // 重名 / 缺文件要报错
  const dupPath = path.join(MSRC, 'dup.json');
  fs.writeFileSync(dupPath, JSON.stringify({
    atlas: 'dup', frames: [
      { file: 'raw_0.png', name: 'same', index: 1 },
      { file: 'raw_1.png', name: 'same', index: 2 }],
  }), 'utf8');
  let dupOut = '';
  try { run(['--manifest', dupPath, '-o', path.join(tmp, 'man_dup')]); }
  catch (e) { dupOut = String(e.stdout || '') + String(e.stderr || '') + String(e.message || ''); }
  check('精灵名重复要报错（不能静默出图）', /重复/.test(dupOut), dupOut.slice(-160));

  const misPath = path.join(MSRC, 'missing.json');
  fs.writeFileSync(misPath, JSON.stringify({
    atlas: 'm', frames: [{ file: 'nope.png', name: 'a', index: 1 }],
  }), 'utf8');
  let misOut = '';
  try { run(['--manifest', misPath, '-o', path.join(tmp, 'man_miss')]); }
  catch (e) { misOut = String(e.stdout || '') + String(e.stderr || '') + String(e.message || ''); }
  check('图片不存在要报错', /不存在/.test(misOut), misOut.slice(-160));

  // 序号全缺 -> 按数组顺序补，名字用 atlas 名 + 补零
  const autoPath = path.join(MSRC, 'auto.json');
  fs.writeFileSync(autoPath, JSON.stringify({
    atlas: 'auto', frames: [0, 1, 2, 3].map(i => ({ file: `raw_${i}.png` })),
  }), 'utf8');
  const AOUT = path.join(tmp, 'man_auto');
  run(['--manifest', autoPath, '-o', AOUT, '--format', 'json-array', '--trim']);
  const as = readJson(path.join(AOUT, 'auto_sequence.json'));
  check('序号全缺时补成 1..4', JSON.stringify(as.frames.map(f => f.index)) === '[1,2,3,4]',
    as.frames.map(f => f.index).join(','));
  check('没给名字时用 atlas 名 + 补零序号',
    JSON.stringify(as.frames.map(f => f.name)) ===
    JSON.stringify(['auto_01', 'auto_02', 'auto_03', 'auto_04']),
    as.frames.map(f => f.name).join(','));
}

console.log('\n[7] 货架式装箱：图元尺寸接近时必须优于 MaxRects');
{
  const MSRC = path.join(tmp, 'shelf_in');
  fs.mkdirSync(MSRC, { recursive: true });
  // 真实素材 Misaka-Sk2 那 17 帧的 trim 尺寸（确定性，不用随机数）
  const TRIM = [[147, 355], [152, 354], [205, 345], [248, 341], [244, 341], [170, 341],
                [169, 338], [166, 335], [166, 334], [175, 333], [152, 332], [187, 316],
                [179, 316], [201, 299], [223, 285], [256, 266], [245, 264]];
  // 不做透明边：让 trim 结果**精确等于**给定尺寸，和 Python 侧口径一致
  let net = 0;
  TRIM.forEach(([tw, th], i) => {
    framePng(path.join(MSRC, `g${String(i).padStart(2, '0')}.png`), tw, th, 0, [40 + i * 13, 90, 160]);
    net += tw * th;
  });
  const common = ['--padding', '2', '--extrude', '1', '--trim', '--rotate', '--format', 'json-array'];
  const A = path.join(tmp, 'shelf_auto');
  const M = path.join(tmp, 'shelf_mr');
  const S = path.join(tmp, 'shelf_sh');
  run([MSRC, '-o', A, '--name', 'a', ...common]);
  run([MSRC, '-o', M, '--name', 'm', '--packer', 'maxrects', ...common]);
  run([MSRC, '-o', S, '--name', 's', '--packer', 'shelf', ...common]);

  const size = d => d.meta.size;
  const a = size(readJson(path.join(A, 'a.json')));
  const m = size(readJson(path.join(M, 'm.json')));
  const sh = size(readJson(path.join(S, 's.json')));
  const dens = z => net / (z.w * z.h);

  check('shelf 与 maxrects 都跑通', !!a && !!m && !!sh, `${!!a}/${!!m}/${!!sh}`);
  // 断言「契约」而不是「谁赢」：两个算法各有胜负（取决于尺寸分布），
  // 要保证的是 auto 把两者都试了、并取到较好的那个。
  check('auto 不差于两者中较好的那个',
    a.w * a.h <= Math.min(m.w * m.h, sh.w * sh.h) * 1.001,
    `auto ${a.w}x${a.h} vs maxrects ${m.w}x${m.h} / shelf ${sh.w}x${sh.h}`);
  check('auto 密度 >= 88%', dens(a) >= 0.88,
    `${a.w}x${a.h} 密度 ${(dens(a) * 100).toFixed(1)}%`);
  // 这条同时是「货架装箱器真的注册上了」的证明 —— pack_ue.js 是子进程，
  // 父进程 require 看不到它的注册；而如果注册失败，--packer shelf 会静默退回
  // MaxRectsBin，版面就跟 --packer maxrects 一模一样了。
  check('shelf 与 maxrects 版面不同（证明货架真的生效了）',
    !(m.w === sh.w && m.h === sh.h), `maxrects ${m.w}x${m.h} / shelf ${sh.w}x${sh.h}`);
}

console.log('\n[8] 旋转：allowRotation 决定姿态，不许被吃掉');
{
  // core 的 PackProcessor.js:198 只传 3 个参数：new packerClass(w, h, combo.allowRotation)。
  // 它靠 combo.allowRotation 把「不旋转 / 允许旋转」当两个组合各跑一遍。
  // 如果我们的 ShelfPacker 把姿态写死（比如构造函数第 4 个参数默认 'never'），
  // 那么 allowRotation=true 的那一轮会被 'never' 吃掉 —— 旋转永远不生效。
  // 这里直接钉住构造契约：只传 3 个参数时，allowRotation 必须能真正促成旋转。
  const { ShelfPacker } = require('../shelf_packer');

  const mk = (w, h, n) => ({ rect: { name: 'x' + n, frame: { w, h }, sourceSize: { w, h } },
                             name: 'x' + n, w, h });
  // 4 根 60x400 的竖条：竖放要 400 高，横放只要 60 —— 行宽够时横放能整行压低
  const tiles = [mk(60, 400, 1), mk(60, 400, 2), mk(60, 400, 3), mk(60, 400, 4)];

  const noRot = new ShelfPacker(420, 4000, false)._shelf(tiles, 420, 4000, null);
  const yesRot = new ShelfPacker(420, 4000, true)._shelf(tiles, 420, 4000, null);

  check('只传 3 个参数、allowRotate=true 时确实产生旋转帧',
    yesRot && yesRot.items.filter(i => i.rot).length === tiles.length,
    yesRot ? `旋转 ${yesRot.items.filter(i => i.rot).length} 帧` : 'null');
  check('allowRotate=false 时一帧都不许旋转',
    noRot && noRot.items.filter(i => i.rot).length === 0,
    noRot ? `旋转 ${noRot.items.filter(i => i.rot).length} 帧` : 'null');
  check('allowRotate 改变了版面（证明姿态真的参与了装箱）',
    noRot && yesRot && (noRot.tw !== yesRot.tw || noRot.th !== yesRot.th),
    `off ${noRot && noRot.tw}x${noRot && noRot.th} / on ${yesRot && yesRot.tw}x${yesRot && yesRot.th}`);
  check('rotateMode 跟随 allowRotate 推导（不被写死成 never）',
    new ShelfPacker(100, 100, true).rotateMode === 'auto' &&
    new ShelfPacker(100, 100, false).rotateMode === 'never',
    `true->${new ShelfPacker(100, 100, true).rotateMode} / false->${new ShelfPacker(100, 100, false).rotateMode}`);
}

console.log(`\n${pass} 通过 / ${fail} 失败`);

fs.rmSync(tmp, { recursive: true, force: true });
process.exit(fail ? 1 : 0);
