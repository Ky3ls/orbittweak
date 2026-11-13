'use strict';
/* Controller display built from the Gamepad Viewer skin parts (Dracula theme, MIT licence,
   see pad/LICENSE.txt), recoloured for OrbitTweak. The same parts are used for the app
   preview (SVG) and rendered to PNGs for the in-game overlay (WPF). */
(function (g) {
  const BASE = (document.currentScript && document.currentScript.src || location.href).replace(/[^/]*$/, '') + 'pad/';
  const cache = {};
  const load = f => cache[f] || (cache[f] = fetch(BASE + f + '.svg').then(r => r.text()).catch(() => ''));

  const PAL = {
    black: { '#282A36': '#1C1D21', '#44475A': '#2B2D33', '#BD93F9': '#3E414A', '#8E6FBB': '#30323A', '#8E6FBC': '#30323A', '#FF79C6': '#25272D', '#121212': '#0E0E10', '#6272A4': '#3A3D46' },
    white: { '#282A36': '#E4E5E9', '#44475A': '#C2C4CB', '#BD93F9': '#F7F7F9', '#8E6FBB': '#D2D3D9', '#8E6FBC': '#D2D3D9', '#FF79C6': '#D9DADF', '#121212': '#8F9198', '#6272A4': '#B5B7BE' },
  };
  function shade(hex, f) {
    const n = parseInt(hex.slice(1), 16), c = [n >> 16, (n >> 8) & 255, n & 255].map(v => Math.round(Math.min(255, v * f)));
    return '#' + c.map(v => v.toString(16).padStart(2, '0')).join('');
  }
  function recolor(svg, body, accent) {
    const map = { ...PAL[body], '#8BE9FD': accent, '#69AFBE': shade(accent, .78) };
    return svg.replace(/#[0-9a-fA-F]{6}\b/g, h => map[h.toUpperCase()] || h);
  }
  const uri = s => 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(s);
  const mod = (a, n) => ((a % n) + n) % n;

  // part: [file, spriteW, spriteH, bgX, bgY, x, y, w, h, flip]
  const L = {
    ps: {
      W: 806, H: 599, base: 'ds4_base', mv: 18,
      fixed: [ // always visible (normal frames)
        ['ds4_face', 224, 112.3, 0, 0, 625, 275, 55, 55], ['ds4_face', 224, 112.3, -57, 0, 682, 217, 55, 55],
        ['ds4_face', 224, 112.3, -113, 0, 567, 217, 55, 55], ['ds4_face', 224, 112.3, 55, 0, 625, 159, 55, 55],
        ['ds4_dpad', 178.5, 104, -37, 0, 136, 181, 36, 52], ['ds4_dpad', 178.5, 104, 0, 0, 136, 255, 36, 52],
        ['ds4_dpad', 178.5, 104, 104, 0, 92, 226, 52, 36], ['ds4_dpad', 178.5, 104, 52, 0, 165, 226, 52, 36],
      ],
      press: {
        a: ['ds4_face', 224, 112.3, 0, 55, 625, 275, 55, 55], b: ['ds4_face', 224, 112.3, -57, 55, 682, 217, 55, 55],
        x: ['ds4_face', 224, 112.3, -113, 55, 567, 217, 55, 55], y: ['ds4_face', 224, 112.3, 55, 55, 625, 159, 55, 55],
        up: ['ds4_dpad', 178.5, 104, -37, 52, 136, 181, 36, 52], down: ['ds4_dpad', 178.5, 104, 0, 52, 136, 255, 36, 52],
        left: ['ds4_dpad', 178.5, 104, 104, 52, 92, 226, 52, 36], right: ['ds4_dpad', 178.5, 104, 52, 52, 165, 226, 52, 36],
        lb: ['ds4_bumper', 98.9, 22.4, 0, 0, 109, 94, 99, 23], rb: ['ds4_bumper', 98.9, 22.4, 0, 0, 598, 94, 99, 23, 1],
        back: ['ds4_start', 28.4, 46.7, 0, 0, 227, 142, 28, 46], start: ['ds4_start', 28.4, 46.7, 0, 0, 551, 142, 28, 46],
      },
      padRect: [276, 125, 254, 145, 12],
      trig: { lt: ['ds4_triggers', 199.9, 90.8, 0, 0, 109, 0, 99, 90], rt: ['ds4_triggers', 199.9, 90.8, 99, 0, 598, 0, 99, 90] },
      sticks: { ls: [228, 308], rs: [484, 308] }, stick: ['ds4_sticks', 286, 94, 94], stickPress: { ls: -96, rs: -192 },
      guide: [403, 362, 19],
    },
    xbox: {
      W: 750, H: 630, base: 'xbox_base', mv: 18,
      fixed: [
        ['xbox_abxy', 195.574, 96.744, 0, 0, 543, 300, 48, 48], ['xbox_abxy', 195.574, 96.744, -49, 0, 593, 250, 48, 48],
        ['xbox_abxy', 195.574, 96.744, -98, 0, 492, 250, 48, 48], ['xbox_abxy', 195.574, 96.744, 48, 0, 543, 199, 48, 48],
      ],
      press: {
        a: ['xbox_abxy', 195.574, 96.744, 0, -48, 543, 305, 48, 48], b: ['xbox_abxy', 195.574, 96.744, -49, -48, 593, 255, 48, 48],
        x: ['xbox_abxy', 195.574, 96.744, -98, -48, 492, 255, 48, 48], y: ['xbox_abxy', 195.574, 96.744, 48, -48, 543, 204, 48, 48],
        up: ['xbox_dpad', 70.659, 128.036, 34, 0, 261, 345, 34, 56], down: ['xbox_dpad', 70.659, 128.036, 0, 0, 261, 400, 34, 56],
        left: ['xbox_dpad', 70.659, 128.036, 0, -93, 223, 383, 55, 35], right: ['xbox_dpad', 70.659, 128.036, 0, -57, 278, 383, 55, 35],
        lb: ['xbox_bumper', 171.154, 62.864, 0, 0, 107, 129, 170, 61], rb: ['xbox_bumper', 171.154, 62.864, 0, 0, 473, 129, 170, 61, 1],
        back: ['xbox_start-select', 67.409, 33.206, 0, 0, 306, 264, 33, 33], start: ['xbox_start-select', 67.409, 33.206, 33, 0, 414, 264, 33, 33],
      },
      trig: { lt: ['xbox_trigger', 88.934, 121.795, 0, 0, 152, 0, 88, 121], rt: ['xbox_trigger', 88.934, 121.795, 0, 0, 510, 0, 88, 121, 1] },
      sticks: { ls: [144, 239], rs: [432, 352] }, stick: ['xbox_stick', 168.77, 83.384, 83], stickNormal: -85, stickPressed: 0,
      guide: [375, 190, 26],
    },
  };

  // one sprite region as nested svg
  function region(src, [f, sw, sh, bx, by, x, y, w, h, flip], dx = 0, dy = 0) {
    const ox = mod(-bx, sw), oy = mod(-by, sh);
    const inner = `<svg x="${x + dx}" y="${y + dy}" width="${w}" height="${h}" viewBox="${ox} ${oy} ${w} ${h}" preserveAspectRatio="none" overflow="hidden"><image href="${src[f]}" width="${sw}" height="${sh}"/></svg>`;
    return flip ? `<g transform="translate(${2 * (x + dx) + w},0) scale(-1,1)">${inner}</g>` : inner;
  }

  async function sources(layout, body, accent) {
    const D = L[layout], files = new Set([D.base, D.stick[0], ...D.fixed.map(p => p[0]), ...Object.values(D.press).map(p => p[0]), ...Object.values(D.trig).map(p => p[0])]);
    const src = {};
    await Promise.all([...files].map(async f => { src[f] = uri(recolor(await load(f), body, accent)); }));
    return src;
  }

  function stickPart(layout, id, pressed) {
    const D = L[layout], [f, sw, sh, w] = D.stick, [x, y] = D.sticks[id];
    const bx = layout === 'ps' ? (pressed ? D.stickPress[id] : 0) : (pressed ? D.stickPressed : D.stickNormal);
    return [f, sw, sh, bx, 0, x, y, w, w];
  }

  async function staticSvg(layout, body, accent) {
    const D = L[layout], src = await sources(layout, body, accent);
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${D.W} ${D.H}" width="${D.W}" height="${D.H}"><image href="${src[D.base]}" width="${D.W}" height="${D.H}"/>${D.fixed.map(p => region(src, p)).join('')}</svg>`;
  }

  // ---------- preview renderer ----------
  function Pad(host) { this.host = host; this.key = null; }
  Pad.prototype.draw = async function (s, cfg) {
    const layout = cfg.layout === 'auto' ? (s && s.t === 'ps' ? 'ps' : 'xbox') : cfg.layout;
    const key = layout + cfg.body + cfg.accent;
    if (this.key !== key) {
      this.key = key;
      const D = L[layout], src = await sources(layout, cfg.body, cfg.accent);
      if (this.key !== key) return;
      let svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${D.W} ${D.H}" style="width:100%;height:auto;display:block"><image href="${src[D.base]}" width="${D.W}" height="${D.H}"/>`;
      for (const [id, p] of Object.entries(D.trig)) svg += `<g data-t="${id}" opacity="0">${region(src, p)}</g>`;
      svg += D.fixed.map(p => region(src, p)).join('');
      for (const [id, p] of Object.entries(D.press)) svg += `<g data-p="${id}" opacity="0">${region(src, p)}</g>`;
      const [gx, gy, gr] = D.guide; svg += `<circle data-p="guide" cx="${gx}" cy="${gy}" r="${gr}" fill="${cfg.accent}" opacity="0"/>`;
      if (D.padRect) { const [x, y, w, h, rr] = D.padRect; svg += `<rect data-p="pad" x="${x}" y="${y}" width="${w}" height="${h}" rx="${rr}" fill="${cfg.accent}" opacity="0"/>`; }
      for (const id of ['ls', 'rs']) svg += `<g data-s="${id}"><g class="n">${region(src, stickPart(layout, id, false))}</g><g class="p" opacity="0">${region(src, stickPart(layout, id, true))}</g></g>`;
      this.host.innerHTML = svg + '</svg>';
      this.mv = D.mv;
    }
    const b = (s && s.b) || {};
    this.host.querySelectorAll('[data-p]').forEach(n => n.setAttribute('opacity', b[n.dataset.p] ? (n.tagName === 'circle' ? .75 : n.tagName === 'rect' ? .5 : 1) : 0));
    this.host.querySelectorAll('[data-t]').forEach(n => n.setAttribute('opacity', Math.min(1, (n.dataset.t === 'lt' ? s?.lt : s?.rt) || 0).toFixed(2)));
    this.host.querySelectorAll('[data-s]').forEach(n => {
      const id = n.dataset.s, ax = id === 'ls' ? s?.lx || 0 : s?.rx || 0, ay = id === 'ls' ? s?.ly || 0 : s?.ry || 0;
      n.setAttribute('transform', `translate(${(ax * this.mv).toFixed(1)},${(ay * this.mv).toFixed(1)})`);
      n.querySelector('.p').setAttribute('opacity', b[id] ? 1 : 0); n.querySelector('.n').setAttribute('opacity', b[id] ? 0 : 1);
    });
  };

  // ---------- PNGs for the in-game overlay ----------
  function toPng(svg, w, h, scale = 1.5) {
    return new Promise(res => {
      const img = new Image();
      img.onload = () => { const c = document.createElement('canvas'); c.width = Math.round(w * scale); c.height = Math.round(h * scale); c.getContext('2d').drawImage(img, 0, 0, c.width, c.height); res(c.toDataURL('image/png').split(',')[1]); };
      img.onerror = () => res('');
      img.src = uri(svg);
    });
  }
  const one = (src, p) => { const [, , , , , , , w, h] = p; const q = [...p]; q[5] = 0; q[6] = 0; return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}">${region(src, q)}</svg>`; };

  async function overlayPack(layout, body, accent) {
    const D = L[layout], src = await sources(layout, body, accent);
    const parts = [];
    for (const [id, p] of Object.entries(D.trig)) parts.push({ id, k: 'trig', x: p[5], y: p[6], w: p[7], h: p[8], png: await toPng(one(src, p), p[7], p[8]) });
    for (const [id, p] of Object.entries(D.press)) parts.push({ id, k: 'img', x: p[5], y: p[6], w: p[7], h: p[8], png: await toPng(one(src, p), p[7], p[8]) });
    const [gx, gy, gr] = D.guide; parts.push({ id: 'guide', k: 'dot', x: gx - gr, y: gy - gr, w: gr * 2, h: gr * 2, col: accent });
    if (D.padRect) { const [x, y, w, h, rr] = D.padRect; parts.push({ id: 'pad', k: 'rect', x, y, w, h, r: rr, col: accent }); }
    for (const id of ['ls', 'rs']) {
      const n = stickPart(layout, id, false), p = stickPart(layout, id, true);
      parts.push({ id, k: 'stick', x: n[5], y: n[6], w: n[7], h: n[8], mv: D.mv, png: await toPng(one(src, n), n[7], n[8]), png2: await toPng(one(src, p), p[7], p[8]) });
    }
    return { w: D.W, h: D.H, art: await toPng(await staticSvg(layout, body, accent), D.W, D.H), parts };
  }

  g.PadArt = { Pad, overlayPack };
})(window);
