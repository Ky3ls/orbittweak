'use strict';
/* Controller art (own drawing) + live highlights.
   Static art is SVG; the moving/pressed parts ("dyn") are described separately so the
   app preview (SVG) and the in-game overlay (WPF, gets a PNG of the art + dyn) look identical. */
(function (g) {
  const W = 600, H = 400;
  // mirror absolute "x,y" pairs of a path around the vertical center line
  const mx = d => d.replace(/(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)/g, (_, x, y) => `${W - x},${y}`).replace(/H(-?\d+(?:\.\d+)?)/g, (_, x) => 'H' + (W - x));

  const XB = {
    body: 'M150,70 C200,52 400,52 450,70 C500,86 540,120 560,190 C585,270 590,330 560,360 C530,388 480,380 455,345 C430,312 410,285 380,280 L220,280 C190,285 170,312 145,345 C120,380 70,388 40,360 C10,330 15,270 40,190 C60,120 100,86 150,70 Z',
    lt: 'M146,64 C152,20 214,8 248,16 C262,20 266,36 260,52 L152,78 Z',
    lb: 'M116,100 C138,68 208,54 262,58 C272,59 274,70 266,76 C214,74 160,84 132,108 Z',
    up: 'M229,206 H251 V236 L240,245 L229,236 Z', down: 'M229,294 H251 V264 L240,255 L229,264 Z',
    left: 'M196,239 V261 H226 L235,250 L226,239 Z', right: 'M284,239 V261 H254 L245,250 L254,239 Z',
    share: 'M291,199 H309 C315,199 315,211 309,211 H291 C285,211 285,199 291,199 Z',
    face: [['y', 430, 132, '#F2B630', 'Y'], ['b', 468, 170, '#E8463A', 'B'], ['a', 430, 208, '#6BBF4E', 'A'], ['x', 392, 170, '#3C8EDB', 'X']],
    ls: [175, 170], rs: [360, 250], dpad: [240, 250]
  };
  const PS = {
    body: 'M160,80 C220,62 380,62 440,80 C500,96 540,140 556,200 C576,280 580,340 548,366 C516,390 470,372 452,336 C438,308 420,292 392,292 L208,292 C180,292 162,308 148,336 C130,372 84,390 52,366 C20,340 24,280 44,200 C60,140 100,96 160,80 Z',
    panel: 'M210,92 L390,92 C404,150 402,236 382,290 L218,290 C198,236 196,150 210,92 Z',
    pad: 'M240,82 H360 C374,82 378,88 378,100 V160 C378,172 372,178 360,178 H240 C228,178 222,172 222,160 V100 C222,88 226,82 240,82 Z',
    lt: 'M150,70 C158,28 216,16 250,24 C264,28 268,44 262,60 L158,86 Z',
    lb: 'M124,108 C148,78 210,66 256,70 C266,71 268,82 260,88 C212,88 166,96 138,116 Z',
    up: 'M152,128 Q152,124 156,124 H174 Q178,124 178,128 V150 L165,162 L152,150 Z',
    down: 'M152,216 Q152,220 156,220 H174 Q178,220 178,216 V194 L165,182 L152,194 Z',
    left: 'M117,159 Q113,159 113,163 V181 Q113,185 117,185 H139 L151,172 L139,159 Z',
    right: 'M213,159 Q217,159 217,163 V181 Q217,185 213,185 H191 L179,172 L191,159 Z',
    back: 'M201,100 H211 C215,100 216,103 216,106 V122 C216,125 215,128 211,128 H201 C197,128 196,125 196,122 V106 C196,103 197,100 201,100 Z',
    face: [['y', 440, 134, '#4ADE9A', 'tri'], ['b', 478, 172, '#F87171', 'cir'], ['a', 440, 210, '#7DB4FF', 'crs'], ['x', 402, 172, '#F49AC2', 'sqr']],
    ls: [232, 252], rs: [368, 252], dpad: [165, 172]
  };

  function colors(layout, body) {
    const white = body === 'white';
    if (layout === 'ps') return white
      ? { b1: '#F7F7F9', b2: '#D7D8DD', edge: '#B9BAC1', panel: '#1B1C21', panel2: '#26272D', pad1: '#E9EAEE', pad2: '#C9CAD0', bump: '#ECEDF0', trig: '#D0D1D6', btn1: '#2A2B31', btn2: '#141519', well: '#0D0D10', dp1: '#E4E5E9', dp2: '#C4C5CB', sym: '#9C9DA6', cap1: '#3A3B42', cap2: '#16171B', capTop: '#202126' }
      : { b1: '#30323A', b2: '#16171B', edge: '#0A0A0C', panel: '#0E0F12', panel2: '#191A1F', pad1: '#2C2E35', pad2: '#1A1B20', bump: '#34363D', trig: '#26282E', btn1: '#2A2B31', btn2: '#111215', well: '#08080A', dp1: '#34363D', dp2: '#1E1F24', sym: '#9C9DA6', cap1: '#3A3B42', cap2: '#16171B', capTop: '#202126' };
    return white
      ? { b1: '#F5F5F7', b2: '#D3D4D9', edge: '#AEB0B7', bump: '#E6E7EA', trig: '#CBCCD1', btn1: '#2E3036', btn2: '#16171B', well: '#B7B8BE', dp1: '#3A3C43', dp2: '#1D1E22', cap1: '#4A4C53', cap2: '#1C1D21', capTop: '#2A2B30', sym: '#E0E0E5' }
      : { b1: '#34363D', b2: '#141518', edge: '#08080A', bump: '#3C3E46', trig: '#2A2C32', btn1: '#2E3036', btn2: '#101114', well: '#0A0A0C', dp1: '#3A3C43', dp2: '#1A1B1F', cap1: '#44464E', cap2: '#18191D', capTop: '#26272C', sym: '#E0E0E5' };
  }

  const sym = (k, x, y, c) => k === 'tri' ? `<path d="M${x},${y - 8} L${x + 8},${y + 6} L${x - 8},${y + 6} Z" fill="none" stroke="${c}" stroke-width="2.6" stroke-linejoin="round"/>`
    : k === 'cir' ? `<circle cx="${x}" cy="${y}" r="7.5" fill="none" stroke="${c}" stroke-width="2.6"/>`
    : k === 'crs' ? `<path d="M${x - 7},${y - 7} L${x + 7},${y + 7} M${x + 7},${y - 7} L${x - 7},${y + 7}" stroke="${c}" stroke-width="2.6" stroke-linecap="round"/>`
    : k === 'sqr' ? `<rect x="${x - 7}" y="${y - 7}" width="14" height="14" rx="1.5" fill="none" stroke="${c}" stroke-width="2.6"/>`
    : `<text x="${x}" y="${y + 7}" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-weight="800" font-size="20" fill="${c}">${k}</text>`;

  // ---------- static art ----------
  function art(layout, body) {
    const G = layout === 'ps' ? PS : XB, C = colors(layout, body), ps = layout === 'ps';
    const well = ([x, y], r) => `<circle cx="${x}" cy="${y}" r="${r}" fill="url(#well)"/><circle cx="${x}" cy="${y}" r="${r}" fill="none" stroke="rgba(255,255,255,.06)" stroke-width="1.5"/>`;
    const btn = (x, y, r) => `<circle cx="${x}" cy="${y}" r="${r + 1.5}" fill="rgba(0,0,0,.45)"/><circle cx="${x}" cy="${y}" r="${r}" fill="url(#btn)"/><circle cx="${x}" cy="${y - r * .35}" rx="0" r="${r * .62}" fill="rgba(255,255,255,.07)"/>`;
    let s = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
<defs>
 <linearGradient id="body" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${C.b1}"/><stop offset="1" stop-color="${C.b2}"/></linearGradient>
 <radialGradient id="shine" cx=".5" cy=".18" r=".6"><stop offset="0" stop-color="#fff" stop-opacity=".16"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient>
 <linearGradient id="bump" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${C.bump}"/><stop offset="1" stop-color="${C.b2}"/></linearGradient>
 <linearGradient id="trig" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${C.trig}"/><stop offset="1" stop-color="${C.edge}"/></linearGradient>
 <radialGradient id="well" cx=".5" cy=".45" r=".55"><stop offset="0" stop-color="${C.well}"/><stop offset=".85" stop-color="${C.well}"/><stop offset="1" stop-color="${C.b2}"/></radialGradient>
 <radialGradient id="btn" cx=".4" cy=".3" r=".8"><stop offset="0" stop-color="${C.btn1}"/><stop offset="1" stop-color="${C.btn2}"/></radialGradient>
 <linearGradient id="dp" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${C.dp1}"/><stop offset="1" stop-color="${C.dp2}"/></linearGradient>
 ${ps ? `<linearGradient id="panel" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${C.panel2}"/><stop offset="1" stop-color="${C.panel}"/></linearGradient>
 <linearGradient id="tpad" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${C.pad1}"/><stop offset="1" stop-color="${C.pad2}"/></linearGradient>` : ''}
 <filter id="sh" x="-20%" y="-20%" width="140%" height="160%"><feDropShadow dx="0" dy="10" stdDeviation="10" flood-opacity=".55"/></filter>
</defs>
<path d="${G.lt}" fill="url(#trig)" stroke="${C.edge}" stroke-width="2"/><path d="${mx(G.lt)}" fill="url(#trig)" stroke="${C.edge}" stroke-width="2"/>
<path d="${G.lb}" fill="url(#bump)" stroke="${C.edge}" stroke-width="2"/><path d="${mx(G.lb)}" fill="url(#bump)" stroke="${C.edge}" stroke-width="2"/>
<g filter="url(#sh)"><path d="${G.body}" fill="url(#body)" stroke="${C.edge}" stroke-width="2.5"/></g>
<path d="${G.body}" fill="url(#shine)"/>`;
    if (ps) {
      s += `<path d="${G.panel}" fill="url(#panel)"/>
<path d="${G.pad}" fill="url(#tpad)" stroke="${C.edge}" stroke-width="1.5"/>
<path d="M216,96 C212,128 213,156 220,176" stroke="#3C82F6" stroke-width="4" stroke-linecap="round" fill="none" opacity=".9"/>
<path d="${mx('M216,96 C212,128 213,156 220,176')}" stroke="#3C82F6" stroke-width="4" stroke-linecap="round" fill="none" opacity=".9"/>
<path d="${G.back}" fill="${C.dp2}"/><path d="${mx(G.back)}" fill="${C.dp2}"/>
<circle cx="300" cy="252" r="13" fill="url(#btn)"/><path d="M293,282 H307 C311,282 311,290 307,290 H293 C289,290 289,282 293,282 Z" fill="${C.dp2}"/>`;
      s += `<circle cx="${G.dpad[0]}" cy="${G.dpad[1]}" r="56" fill="rgba(0,0,0,.08)"/>`;
      for (const k of ['up', 'down', 'left', 'right']) s += `<path d="${G[k]}" fill="url(#dp)" stroke="${C.edge}" stroke-width="1.2"/>`;
    } else {
      s += `<circle cx="${G.dpad[0]}" cy="${G.dpad[1]}" r="47" fill="url(#well)"/>
<path d="M229,206 H251 V239 H284 V261 H251 V294 H229 V261 H196 V239 H229 Z" fill="url(#dp)" stroke="${C.edge}" stroke-width="1.5" stroke-linejoin="round"/>
<circle cx="240" cy="250" r="7" fill="rgba(0,0,0,.35)"/>
<circle cx="300" cy="118" r="23" fill="url(#btn)" stroke="rgba(255,255,255,.18)" stroke-width="2"/><circle cx="300" cy="118" r="12" fill="none" stroke="rgba(255,255,255,.55)" stroke-width="3"/>
${btn(262, 170, 11)}<rect x="256" y="165" width="8" height="7" rx="1" fill="none" stroke="${C.sym}" stroke-width="1.6"/><rect x="260" y="168" width="8" height="7" rx="1" fill="none" stroke="${C.sym}" stroke-width="1.6"/>
${btn(338, 170, 11)}<path d="M332,166 H344 M332,170 H344 M332,174 H344" stroke="${C.sym}" stroke-width="1.8" stroke-linecap="round"/>
<path d="${XB.share}" fill="url(#btn)"/>`;
    }
    s += well(G.ls, ps ? 42 : 44) + well(G.rs, ps ? 42 : 44);
    for (const [, x, y, col, k] of G.face) s += btn(x, y, ps ? 20 : 19) + sym(k, x, y, col);
    // unique ids, so several controllers on one page never share gradients
    const u = 'g' + Math.random().toString(36).slice(2, 7);
    return (s + '</svg>').replace(/id="(\w+)"/g, `id="$1${u}"`).replace(/url\(#(\w+)\)/g, `url(#$1${u})`);
  }

  // ---------- live parts ----------
  function dyn(layout) {
    const G = layout === 'ps' ? PS : XB, ps = layout === 'ps', d = [];
    d.push({ id: 'lt', k: 'trig', d: G.lt }, { id: 'rt', k: 'trig', d: mx(G.lt) }, { id: 'lb', k: 'btn', d: G.lb }, { id: 'rb', k: 'btn', d: mx(G.lb) });
    for (const k of ['up', 'down', 'left', 'right']) d.push({ id: k, k: 'btn', d: G[k] });
    for (const [id, x, y, col] of G.face) d.push({ id, k: 'circle', cx: x, cy: y, r: ps ? 20 : 19, col });
    if (ps) d.push({ id: 'pad', k: 'btn', d: PS.pad }, { id: 'back', k: 'btn', d: PS.back }, { id: 'start', k: 'btn', d: mx(PS.back) }, { id: 'guide', k: 'circle', cx: 300, cy: 252, r: 13 });
    else d.push({ id: 'back', k: 'circle', cx: 262, cy: 170, r: 11 }, { id: 'start', k: 'circle', cx: 338, cy: 170, r: 11 }, { id: 'guide', k: 'circle', cx: 300, cy: 118, r: 23 });
    d.push({ id: 'ls', k: 'stick', cx: G.ls[0], cy: G.ls[1], r: 31, mv: 13 }, { id: 'rs', k: 'stick', cx: G.rs[0], cy: G.rs[1], r: 31, mv: 13 });
    return d;
  }

  function bbox(d) {
    const n = (d.match(/-?\d+(?:\.\d+)?,-?\d+(?:\.\d+)?/g) || []).map(t => t.split(',').map(Number));
    const xs = n.map(v => v[0]), ys = n.map(v => v[1]);
    return { x: Math.min(...xs), y: Math.min(...ys), width: Math.max(...xs) - Math.min(...xs), height: Math.max(...ys) - Math.min(...ys) };
  }

  // ---------- preview renderer (SVG) ----------
  function Pad(host) { this.host = host; this.key = null; }
  Pad.prototype.draw = function (s, cfg) {
    const layout = cfg.layout === 'auto' ? (s && s.t === 'ps' ? 'ps' : 'xbox') : cfg.layout;
    const C = colors(layout, cfg.body), key = layout + cfg.body;
    if (this.key !== key) {
      this.key = key; this.dyn = dyn(layout);
      const NS = 'http://www.w3.org/2000/svg';
      this.host.innerHTML = art(layout, cfg.body).replace('</svg>', '<g id="dynL"></g></svg>');
      const svg = this.host.querySelector('svg'); svg.removeAttribute('width'); svg.removeAttribute('height'); svg.style.width = '100%'; svg.style.height = 'auto';
      const gl = svg.querySelector('#dynL');
      this.nodes = {};
      for (const p of this.dyn) {
        if (p.k === 'trig') {
          const id = 'tc' + p.id + Math.random().toString(36).slice(2, 6);
          gl.insertAdjacentHTML('beforeend', `<clipPath id="${id}"><path d="${p.d}"/></clipPath><rect class="lvl" clip-path="url(#${id})" x="0" y="0" width="${W}" height="0" opacity=".88"/>`);
          this.nodes[p.id] = { p, n: gl.lastElementChild, box: null };
        } else if (p.k === 'stick') {
          gl.insertAdjacentHTML('beforeend', `<g><circle cx="${p.cx}" cy="${p.cy + 3}" r="${p.r + 2}" fill="rgba(0,0,0,.5)"/><circle cx="${p.cx}" cy="${p.cy}" r="${p.r}" fill="${C.cap1}" stroke="${C.edge}" stroke-width="2"/><circle cx="${p.cx}" cy="${p.cy}" r="${p.r - 8}" fill="${C.capTop}"/><circle cx="${p.cx}" cy="${p.cy}" r="${p.r - 8}" fill="none" stroke="rgba(255,255,255,.08)" stroke-width="2"/><circle class="ring" cx="${p.cx}" cy="${p.cy}" r="${p.r - 1}" fill="none" stroke-width="4" opacity="0"/></g>`);
          this.nodes[p.id] = { p, n: gl.lastElementChild };
        } else if (p.k === 'circle') {
          gl.insertAdjacentHTML('beforeend', `<circle cx="${p.cx}" cy="${p.cy}" r="${p.r}" opacity="0"/>`); this.nodes[p.id] = { p, n: gl.lastElementChild };
        } else { gl.insertAdjacentHTML('beforeend', `<path d="${p.d}" opacity="0"/>`); this.nodes[p.id] = { p, n: gl.lastElementChild }; }
      }
    }
    const b = (s && s.b) || {}, acc = cfg.accent;
    for (const id in this.nodes) {
      const { p, n } = this.nodes[id], col = cfg.color === 'orig' && p.col ? p.col : acc;
      if (p.k === 'trig') {
        const v = Math.max(0, Math.min(1, id === 'lt' ? s?.lt || 0 : s?.rt || 0));
        const bb = this.nodes[id].box || (this.nodes[id].box = bbox(p.d));
        n.setAttribute('y', bb.y + bb.height * (1 - v)); n.setAttribute('height', bb.height * v); n.setAttribute('fill', acc);
      } else if (p.k === 'stick') {
        const ax = id === 'ls' ? s?.lx || 0 : s?.rx || 0, ay = id === 'ls' ? s?.ly || 0 : s?.ry || 0;
        n.setAttribute('transform', `translate(${(ax * p.mv).toFixed(1)},${(ay * p.mv).toFixed(1)})`);
        const ring = n.querySelector('.ring'); ring.setAttribute('stroke', acc); ring.setAttribute('opacity', b[id] ? 1 : 0);
      } else { n.setAttribute('fill', col); n.setAttribute('opacity', b[id] ? .9 : 0); }
    }
  };

  // ---------- PNG of the art for the in-game overlay ----------
  function png(layout, body, scale = 2) {
    return new Promise(res => {
      const img = new Image();
      img.onload = () => {
        const c = document.createElement('canvas'); c.width = W * scale; c.height = H * scale;
        c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
        res(c.toDataURL('image/png').split(',')[1]);
      };
      img.onerror = () => res('');
      img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(art(layout, body));
    });
  }

  g.PadArt = { art, dyn, png, colors, Pad, W, H };
})(window);
