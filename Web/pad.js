'use strict';
/* Controller display renderer (SVG). Used by the app preview and the OBS browser source.
   Geometry comes from the host (PadGeo), so overlay, preview and OBS look identical. */
(function (g) {
  const NS = 'http://www.w3.org/2000/svg';
  const el = (t, a) => { const e = document.createElementNS(NS, t); for (const k in a) e.setAttribute(k, a[k]); return e; };

  function PadSVG(host, geo) {
    this.host = host; this.geo = geo; this.layout = null; this.nodes = {};
  }
  PadSVG.prototype.build = function (layout, cfg) {
    this.layout = layout;
    const white = cfg.body === 'white';
    const C = this.c = {
      body: white ? '#ECECEF' : '#17171B', bodyLine: white ? '#B9B9C0' : '#2E2E36', idle: white ? '#C6C6CC' : '#2C2C33',
      deep: white ? '#AEAEB5' : '#0D0D10', line: white ? '#9A9AA2' : '#3A3A44', cap: white ? '#F7F7F9' : '#3B3B44',
      capLine: white ? '#9A9AA2' : '#55555F', txt: white ? '#4A4A52' : '#C9C9D1'
    };
    const svg = el('svg', { viewBox: '0 0 340 232', width: '100%', height: '100%' });
    this.nodes = {};
    for (const p of this.geo[layout]) {
      if (p.k === 'body') svg.append(el('path', { d: this.geo.body, fill: C.body, stroke: C.bodyLine, 'stroke-width': 2 }));
      else if (p.k === 'trig') {
        const id = 'clip' + p.id + Math.random().toString(36).slice(2, 7);
        const cp = el('clipPath', { id }); const r = el('rect', { x: p.x, y: p.y + p.h, width: p.w, height: 0 }); cp.append(r); svg.append(cp);
        svg.append(el('rect', { x: p.x, y: p.y, width: p.w, height: p.h, rx: p.r, fill: C.idle, stroke: C.line, 'stroke-width': 1.5 }));
        const f = el('rect', { x: p.x, y: p.y, width: p.w, height: p.h, rx: p.r, 'clip-path': `url(#${id})` }); svg.append(f);
        this.nodes[p.id] = { p, fill: f, clip: r };
      } else if (p.k === 'rect') { const n = el('rect', { x: p.x, y: p.y, width: p.w, height: p.h, rx: p.r, fill: C.idle, stroke: C.line, 'stroke-width': 1.5 }); svg.append(n); this.nodes[p.id] = { p, n }; }
      else if (p.k === 'circle') { const n = el('circle', { cx: p.x, cy: p.y, r: p.r, fill: C.idle, stroke: C.line, 'stroke-width': 1.5 }); svg.append(n); this.nodes[p.id] = { p, n }; }
      else if (p.k === 'base') svg.append(el('circle', { cx: p.x, cy: p.y, r: p.r, fill: C.deep, stroke: C.line, 'stroke-width': 1.5 }));
      else if (p.k === 'stick') {
        const gr = el('g', {}); const n = el('circle', { cx: p.x, cy: p.y, r: p.r, fill: C.cap, stroke: C.capLine, 'stroke-width': 2 });
        gr.append(n, el('circle', { cx: p.x, cy: p.y, r: p.r - 5, fill: 'none', stroke: white ? '#C9C9CF' : '#24242A', 'stroke-width': 1.5 }));
        svg.append(gr); this.nodes[p.id] = { p, n, gr };
      } else if (p.k === 'face') {
        const n = el('circle', { cx: p.x, cy: p.y, r: p.r, fill: C.idle, stroke: C.line, 'stroke-width': 1.5 });
        const t = el('text', { x: p.x, y: p.y + 4.3, 'text-anchor': 'middle', 'font-size': 12, 'font-weight': 700, 'font-family': 'Segoe UI Symbol, Segoe UI, sans-serif' }); t.textContent = p.txt;
        svg.append(n, t); this.nodes[p.id] = { p, n, t };
      }
    }
    this.host.innerHTML = ''; this.host.append(svg);
  };
  PadSVG.prototype.draw = function (s, cfg) {
    const layout = cfg.layout === 'auto' ? (s && s.t === 'ps' ? 'ps' : 'xbox') : cfg.layout;
    const key = layout + cfg.body;
    if (this.key !== key) { this.key = key; this.build(layout, cfg); }
    const C = this.c, b = (s && s.b) || {}, acc = cfg.color === 'theme' || cfg.color === 'orig' ? cfg.accent : cfg.color;
    for (const id in this.nodes) {
      const o = this.nodes[id], p = o.p;
      if (p.k === 'trig') { const v = Math.max(0, Math.min(1, id === 'lt' ? s?.lt || 0 : s?.rt || 0)); o.clip.setAttribute('y', p.y + p.h * (1 - v)); o.clip.setAttribute('height', p.h * v); o.fill.setAttribute('fill', acc); }
      else if (p.k === 'stick') {
        const ax = id === 'ls' ? s?.lx || 0 : s?.rx || 0, ay = id === 'ls' ? s?.ly || 0 : s?.ry || 0;
        o.gr.setAttribute('transform', `translate(${ax * 10},${ay * 10})`); o.n.setAttribute('fill', b[id] ? acc : C.cap);
      } else if (p.k === 'face') {
        const col = cfg.color === 'orig' ? p.col : acc;
        o.n.setAttribute('fill', b[id] ? col : C.idle);
        o.t.setAttribute('fill', b[id] ? (cfg.body === 'white' ? '#FFFFFF' : '#101014') : cfg.color === 'orig' ? p.col : C.txt);
      } else o.n.setAttribute('fill', b[id] ? acc : C.idle);
    }
  };
  g.PadSVG = PadSVG;
})(window);
