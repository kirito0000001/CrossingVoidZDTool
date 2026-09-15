/**
 * shelf_packer.js —— 给 free-tex-packer-core 挂一个「货架式」装箱器
 *
 * 为什么要它：core 只有 MaxRects 族。而 **MaxRects 在图元尺寸接近时表现反而差** ——
 * 实测同一批 17 帧 Misaka-Sk2（尺寸都在 150~260 × 260~360）：
 *     MaxRectsOptimal(5 个启发式全试)  1020×1218  密度 86.1%
 *     货架式                            1123×1019  密度 93.4%   ← 好 8%
 *     TexturePacker                     1132× 996  密度 94.8%
 * 拆 TexturePacker 官方 DLL 也印证了：它有 `AlgorithmShelf`，和 `AlgorithmMaxRects` 并列，
 * 还有 `--pack-mode Fast/Good/Best` 控制搜多狠、"Trying %u different sets of parameters"。
 *
 * 怎么挂上去：core 的 `packers/index.js` 把内部 list 导出了，而 `getPackerByType()` 和
 * OptimalPacker 的 `getAllPackers()` 都遍历这个 list。所以 push 进去就自动被认、
 * 也会自动被 OptimalPacker 纳入「全试一遍」的搜索。
 *
 * 关键设计：**宽度搜索放在装箱器内部做**（纯计算，不重渲染贴图）。
 * 货架对行宽极其敏感 —— 差几个像素就换一种断行方式、面积差 5% 以上；
 * 如果靠外层反复调 packAsync 去试宽度，每次都要重渲染一张贴图，几百个候选根本跑不动。
 * 装箱器内部做就没这个成本：core 只渲染最终选中的那一次。
 */

let Packer;
try {
  Packer = require('free-tex-packer-core/packers/Packer');
} catch (e) {
  Packer = class {};   // 拿不到基类也能跑，我们只用静态属性
}

const METHOD = {
  SortByHeight: 'SortByHeight',
  SortByHeightThenWidth: 'SortByHeightThenWidth',
  SortByWidth: 'SortByWidth',
  SortByArea: 'SortByArea',
  SortByLongSide: 'SortByLongSide',
};

// 排序键必须是**确定性**的：破平一律用 name，不要依赖「谁先传进来」。
// 否则同一份素材在两个工具（Python / Node）里会因为输入顺序不同而排出差 5% 的结果。
// 这几个定义要和 ue_atlas.py 的 Shelf.KEYS 一一对应。
const byName = (a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0);

const SORTERS = {
  [METHOD.SortByHeight]: (a, b) => (b.h - a.h) || byName(a, b),
  [METHOD.SortByHeightThenWidth]: (a, b) => (b.h - a.h) || (b.w - a.w),
  [METHOD.SortByWidth]: (a, b) => (b.w - a.w) || (b.h - a.h),
  [METHOD.SortByArea]: (a, b) => (b.w * b.h - a.w * a.h) || (b.h - a.h),
  [METHOD.SortByLongSide]: (a, b) => (Math.max(b.w, b.h) - Math.max(a.w, a.h)) || (b.w - a.w),
};

// 长宽比惩罚指数，和 ue_atlas.py 保持一致（0.4 是实测值）
const ASPECT_POWER = 0.4;

class ShelfPacker extends Packer {
  constructor(width, height, allowRotate = false, rotateMode = null) {
    super();
    this.binWidth = width;
    this.binHeight = height;
    this.allowRotate = allowRotate;
    // 'never' | 'auto' | 'all' —— 见 _shelf() 的说明。
    //
    // 注意 core 的调用方式：`PackProcessor.js:198` 只传 3 个参数
    // （`new packerClass(width, height, combo.allowRotation)`），它自己用
    // `combo.allowRotation` 把「不旋转 / 允许旋转」当成两个组合各跑一遍
    // （见同文件 148~153 行的 getAllPackers）。所以这里**不能**要求外部传第 4 个参数，
    // 否则 allowRotation=true 的那一轮会被 'never' 吃掉、永远转不起来（这个坑踩过）。
    // 不显式给 rotateMode 时，跟着 allowRotate 走：允许旋转就 'auto'。
    this.rotateMode = ['never', 'auto', 'all'].includes(rotateMode)
      ? rotateMode
      : (allowRotate ? 'auto' : 'never');
  }

  static get type() { return 'ShelfPacker'; }
  static get defaultMethod() { return METHOD.SortByHeight; }
  static get methods() { return METHOD; }

  static getMethodProps(id = '') {
    switch (id) {
      case METHOD.SortByHeight: return { name: 'Sort by height', description: 'Sort sprites by height, tallest first' };
      case METHOD.SortByHeightThenWidth: return { name: 'Sort by height, then width', description: 'Sort by height and break ties by width' };
      case METHOD.SortByWidth: return { name: 'Sort by width', description: 'Sort sprites by width, widest first' };
      case METHOD.SortByArea: return { name: 'Sort by area', description: 'Sort sprites by their area (width*height)' };
      case METHOD.SortByLongSide: return { name: 'Sort by long side', description: 'Sort by the longer edge' };
      default: return { name: 'Unknown', description: '' };
    }
  }

  static getMethodByType(type) {
    type = String(type).toLowerCase();
    for (const name of Object.keys(METHOD)) {
      if (type === name.toLowerCase()) return METHOD[name];
    }
    return null;
  }

  /**
   * 在给定行宽 W 下排一遍货架。
   *
   * 旋转的唯一价值是**压低行高**：行高 = 行内最高那张，整行都按它留空间。
   * 所以：
   *   - 塞进已有行时**不旋转** —— 行高已经定了，转了只多占宽度，纯亏；
   *   - 只有「开新行」时才值得考虑横放（高变矮 -> 整行变矮）。
   * 与 Python 侧 `ue_atlas.py` 的 `Shelf.pack_all()` 保持同一套语义。
   *
   * @returns {null | {tw:number, th:number, items:Array}} 紧凑尺寸 + 每项的位置
   */
  _shelf(tiles, W, H, sortFn) {
    const rows = [];          // {y, rowH, usedX}
    const items = [];
    let right = 0, bottom = 0;
    const rotateAll = this.rotateMode === 'all';

    for (const t of tiles) {
      const upright = [t.w, t.h, false];
      const sideways = (this.allowRotate && t.w !== t.h) ? [t.h, t.w, true] : null;

      // 1) 先试塞进已有行，不旋转
      let pick = null;
      for (const row of rows) {
        const [cw, ch, rot] = upright;
        if (ch <= row.rowH && row.usedX + cw <= W) { pick = { row, cw, ch, rot }; break; }
      }

      // 2) 开新行 —— 这里旋转才起作用
      if (!pick) {
        // 换行：行高就是这一行第一张的高度
        const y = rows.length ? rows[rows.length - 1].y + rows[rows.length - 1].rowH : 0;
        let cands = [upright];
        if (sideways) {
          const fits = sideways[0] <= W;
          if (rotateAll && fits) cands = [sideways, upright];
          else if (this.rotateMode === 'auto' && fits && sideways[1] < upright[1]) {
            cands = [sideways, upright];
          }
        }
        for (const [cw, ch, rot] of cands) {
          if (cw <= W && y + ch <= H) {
            const row = { y, rowH: ch, usedX: 0 };
            rows.push(row);
            pick = { row, cw, ch, rot };
            break;
          }
        }
      }
      if (!pick) return null;              // 这个宽度装不下

      const { row, cw, ch, rot } = pick;
      items.push({ tile: t, x: row.usedX, y: row.y, rot });
      row.usedX += cw;
      right = Math.max(right, row.usedX);
      bottom = Math.max(bottom, row.y + ch);
    }
    return { tw: right, th: bottom, items };
  }

  /**
   * 装箱。注意 core 交给我们的 rect.frame.w/h **已经膨胀过**
   * `padding*2 + extrude*2`，所以这里按 tile 紧贴摆放就行，不用再留缝。
   */
  pack(data, method) {
    if (!data || !data.length) return [];

    const sortFn = SORTERS[method] || SORTERS[METHOD.SortByHeight];
    const tiles = data.map(r => ({
      rect: r,
      name: String(r.name || ''),
      w: r.frame.w,
      h: r.frame.h,
      area: r.frame.w * r.frame.h,
      base: { x: r.frame.x, y: r.frame.y, w: r.frame.w, h: r.frame.h },
    }));
    const ordered = [...tiles].sort(sortFn);

    const maxW = Math.max(1, this.binWidth);
    const maxH = Math.max(1, this.binHeight);
    const minTileW = Math.min(...ordered.map(t => Math.min(t.w, this.allowRotate ? t.h : t.w)));

    const totalArea = ordered.reduce((s, t) => s + t.area, 0) || 1;
    const side = Math.max(1, Math.round(Math.sqrt(totalArea)));

    // 候选行宽：基准附近**按 1 像素**细扫（货架对宽度太敏感），两端再补几个粗的
    const lo = Math.max(minTileW, Math.floor(side * 0.70));
    const hi = Math.min(maxW, Math.ceil(side * 1.45));
    const cands = new Set();
    for (let w = lo; w <= hi; w++) cands.add(w);
    for (const f of [0.45, 0.55, 0.65, 1.45, 1.6, 1.9, 2.4]) {
      const w = Math.round(side * f);
      if (w >= minTileW && w <= maxW) cands.add(w);
    }
    cands.add(maxW);

    let best = null;
    for (const W of cands) {
      const r = this._shelf(ordered, W, maxH, sortFn);
      if (!r) continue;
      const ratio = Math.max(r.tw, r.th) / Math.min(r.tw, r.th);
      const score = r.tw * r.th * Math.pow(ratio, ASPECT_POWER);
      if (!best || score < best.score) best = { score, ...r, W };
    }

    // 一个都装不下：至少要放下一张，否则 PackProcessor 的 while 循环会卡死
    if (!best) {
      return this._placeFallback(data, ordered, sortFn);
    }

    const placed = [];
    for (const it of best.items) {
      const r = it.tile.rect;
      r.frame.x = it.x;
      r.frame.y = it.y;
      if (it.rot) r.rotated = true;
      placed.push(r);
    }
    for (const r of placed) {
      const i = data.indexOf(r);
      if (i >= 0) data.splice(i, 1);
    }
    return placed;
  }

  /** 兜底：按当前宽度只放得下几张就放几张，剩下的留给 core 开新页。 */
  _placeFallback(data, ordered, sortFn) {
    const W = this.binWidth, H = this.binHeight;
    const rows = [];
    const placed = [];
    for (const t of ordered) {
      const cands = [[t.w, t.h, false]];
      if (this.allowRotate && t.w !== t.h) cands.push([t.h, t.w, true]);
      let pick = null;
      for (const row of rows) {
        for (const [cw, ch, rot] of cands) {
          if (ch <= row.rowH && row.usedX + cw <= W) { pick = { row, cw, ch, rot }; break; }
        }
        if (pick) break;
      }
      if (!pick) {
        const y = rows.length ? rows[rows.length - 1].y + rows[rows.length - 1].rowH : 0;
        for (const [cw, ch, rot] of cands) {
          if (cw <= W && y + ch <= H) {
            const row = { y, rowH: ch, usedX: 0 };
            rows.push(row);
            pick = { row, cw, ch, rot };
            break;
          }
        }
      }
      if (!pick) continue;
      const r = t.rect;
      r.frame.x = pick.row.usedX;
      r.frame.y = pick.row.y;
      if (pick.rot) r.rotated = true;
      pick.row.usedX += pick.cw;
      placed.push(r);
    }
    for (const r of placed) {
      const i = data.indexOf(r);
      if (i >= 0) data.splice(i, 1);
    }
    return placed;
  }
}

/** 把 ShelfPacker 注册进 core 的装箱器列表（幂等）。 */
function registerShelfPacker() {
  const mod = require('free-tex-packer-core/packers');
  if (!mod.list.some(p => p.type === ShelfPacker.type)) {
    mod.list.push(ShelfPacker);
  }
  return mod.list;
}

module.exports = { ShelfPacker, registerShelfPacker, SHELF_METHODS: METHOD };
