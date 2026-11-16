'use strict';
/* OrbitTweak – frontend. Talks to the C# host via WebView2 postMessage;
   in a normal browser a mock backend supplies demo data. */
const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const ico = (id, cls = 'i') => `<svg class="${cls}"><use href="#${id}"/></svg>`;
const nf1 = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const bytes = b => { if (b == null || b < 0) return '—'; const u = ['B', 'KB', 'MB', 'GB', 'TB']; let i = 0; while (b >= 1024 && i < 4) { b /= 1024; i++; } return (i ? nf1.format(b) : b) + ' ' + u[i]; };
const sleep = ms => new Promise(r => setTimeout(r, ms));

const HOST = !!(window.chrome && window.chrome.webview);
let rid = 0; const pending = {};
function api(cmd, args = {}) {
  if (!HOST) return Mock.call(cmd, args);
  return new Promise((res, rej) => { const id = ++rid; pending[id] = { res, rej }; window.chrome.webview.postMessage({ id, cmd, args }); });
}
if (HOST) window.chrome.webview.addEventListener('message', e => {
  const m = e.data;
  if (m.id && pending[m.id]) { const p = pending[m.id]; delete pending[m.id]; m.ok ? p.res(m.data) : p.rej(new Error(m.error || 'Fehler')); }
  else if (m.event) onEvent(m.event, m.data);
});

const S = { tweaks: [], settings: {}, fivem: {}, freed: 0, cpuHist: [], boosting: false, gfx: null, gfxEdit: {}, servers: [], clean: [], health: null };

/* ======================= THEMES ======================= */
const THEMES = {
  ember: ['Ember', '#FF7A3D', '#FFB067'],
  rose: ['Rosé', '#FF5C8A', '#FFA3BD'],
  violet: ['Violet', '#8B7BFF', '#C9B8FF'],
  ocean: ['Ocean', '#3D9BFF', '#7FD4FF'],
  mint: ['Mint', '#2DD4A0', '#9AF2D4'],
  gold: ['Gold', '#F5B53D', '#FFE08A'],
  mono: ['Mono', '#E8E8EC', '#A8A8B3'],
  crimson: ['Crimson', '#FF3B4E', '#FF8A7A'],
  sunset: ['Sunset', '#FF6B3D', '#FFC93D'],
  magenta: ['Magenta', '#E040FB', '#F5A3FF'],
  sky: ['Sky', '#38BDF8', '#A5F3FC'],
  teal: ['Teal', '#14B8A6', '#5EEAD4'],
  lime: ['Lime', '#84CC16', '#D9F99D'],
  copper: ['Copper', '#D97745', '#F2B07E'],
  ice: ['Ice', '#93C5FD', '#E0F2FE'],
  winter: ['Winter', '#6FC3FF', '#EAF8FF'],
  anime: ['Anime', '#FF6FB5', '#B79CFF'],
};
const THEME_ALIAS = { inferno: 'ember', blood: 'rose', toxic: 'mint', nebula: 'violet' };
const themeKey = k => THEMES[k] ? k : THEMES[THEME_ALIAS[k]] ? THEME_ALIAS[k] : 'ember';
const hexRgb = h => [1, 3, 5].map(i => parseInt(h.substr(i, 2), 16)).join(',');
/** Themes that are more than a colour: their own background, cards, banner and moving effects. */
const SCENE_THEMES = ['winter', 'anime'];
let CUR_THEME = 'ember';
function applyTheme(k) {
  const key = CUR_THEME = themeKey(k), t = THEMES[key], root = document.documentElement, r = root.style;
  r.setProperty('--a', t[1]); r.setProperty('--b', t[2]);
  r.setProperty('--a-rgb', hexRgb(t[1])); r.setProperty('--b-rgb', hexRgb(t[2]));
  SCENE_THEMES.forEach(s => root.classList.toggle('th-' + s, key === s));
  if (key === 'winter') { r.setProperty('--icicles', ICICLES ||= icicles()); r.setProperty('--snowcap', SNOWCAP ||= snowCap()); }
  if (!$('.amb .aur')) $('.amb')?.insertAdjacentHTML('beforeend', '<i class="aur"></i>');
  fxStart(key);
  if (S.settings && S.settings.accent !== undefined && typeof applyBg === 'function') applyBg();   // the anime theme paints its own banner
  if ($('#bgGrid')?.children.length) bgGrid();
  restSync();   // newly started endless animations pause too if the page is resting
}

/* ======================= THEME EFFECTS (winter snow, anime petals and dusk scene) ======================= */
// Everything that moves here only changes `transform` on its own layer: the GPU moves the finished picture without
// painting anything again, so a whole screen of snow costs about as much as one moving icon. The layer exists only
// while such a theme is on, pauses with the rest of the page and can be switched off (Settings → Design).
const svgUrl = s => `url('data:image/svg+xml,${encodeURIComponent(s)}')`;   // (the pictures contain no ' themselves)
/** Small repeatable random numbers, so the painted pictures look the same on every start. */
function seeded(seed) {
  return () => { seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
}
const f1 = v => v.toFixed(1);
/** One square of snow that repeats seamlessly: soft dots and, in the near layer, a few six-armed crystals. */
function flakeTile(size, n, r0, r1, crystals, seed) {
  const R = seeded(seed), m = r1 * 3; let s = '';
  for (let i = 0; i < n; i++) s += `<circle cx="${f1(m + R() * (size - 2 * m))}" cy="${f1(m + R() * (size - 2 * m))}" r="${(r0 + R() * (r1 - r0)).toFixed(2)}" fill="url(#g)" opacity="${(.55 + R() * .45).toFixed(2)}"/>`;
  for (let i = 0; i < crystals; i++) {
    const r = 5 + R() * 3.5; let arms = '';
    for (let k = 0; k < 6; k++) arms += `<path transform="rotate(${k * 60})" d="M0 0V${f1(-r)}M0 ${f1(-r * .55)}l${f1(-r * .3)} ${f1(-r * .26)}M0 ${f1(-r * .55)}l${f1(r * .3)} ${f1(-r * .26)}"/>`;
    s += `<g transform="translate(${f1(14 + R() * (size - 28))} ${f1(14 + R() * (size - 28))}) rotate(${(R() * 60).toFixed(0)})" stroke="#fff" stroke-width="1.1" stroke-linecap="round" fill="none" opacity=".8">${arms}</g>`;
  }
  return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}"><defs><radialGradient id="g"><stop offset="0" stop-color="#fff"/><stop offset=".5" stop-color="#fff" stop-opacity=".85"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient></defs>${s}</svg>`);
}
const PIECE = {
  flake: svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20"><defs><radialGradient id="g"><stop offset="0" stop-color="#fff"/><stop offset=".35" stop-color="#fff" stop-opacity=".8"/><stop offset="1" stop-color="#d6ecff" stop-opacity="0"/></radialGradient></defs><circle cx="10" cy="10" r="10" fill="url(#g)"/></svg>'),
  petal: svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 24"><defs><linearGradient id="p" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFE6F1"/><stop offset=".55" stop-color="#FFB6D4"/><stop offset="1" stop-color="#FF84B8"/></linearGradient></defs><path d="M10 23C3.5 18.5.5 12 2.6 6 4.2 1.8 7.6 1 10 4.2 12.4 1 15.8 1.8 17.4 6 19.5 12 16.5 18.5 10 23Z" fill="url(#p)"/><path d="M10 21V9" stroke="#FF8DBE" stroke-width=".8" opacity=".55"/></svg>'),
};
// layers: [tile size, dots, smallest radius, largest radius, crystals, opacity, sway seconds]
const FXCFG = {
  winter: { piece: 'flake', n: 9, size: [7, 15], dur: [11, 19], layers: [[260, 34, .5, 1.1, 0, .55, 9], [360, 16, 1.1, 2, 0, .75, 7], [520, 7, 2, 3.2, 2, .7, 5.5]] },
  anime: { piece: 'petal', n: 16, size: [11, 18], dur: [10, 17], layers: [[360, 9, .8, 1.5, 0, .3, 8]] },
};
const FX = { key: null };
function fxStart(k) {
  const cfg = S.settings.fx !== false && FXCFG[k];
  let fx = $('#fx');
  if (!cfg) { fx?.remove(); FX.key = null; return; }
  if (fx && FX.key === k) return;
  fx?.remove(); FX.key = k;
  fx = document.createElement('div'); fx.className = 'fx'; fx.id = 'fx';
  fx.style.setProperty('--piece', PIECE[cfg.piece]);
  cfg.layers.forEach(([size, n, r0, r1, cr, op, sway], i) => {
    const sw = document.createElement('div'); sw.className = 'fx-sw t' + size;
    sw.style.cssText = `opacity:${op};animation-duration:${sway}s;animation-delay:${-i * 2.3}s`;
    const tile = document.createElement('i'); tile.style.backgroundImage = flakeTile(size, n, r0, r1, cr, 11 + i * 7);
    sw.appendChild(tile); fx.appendChild(sw);
  });
  const R = Math.random;
  for (let i = 0; i < cfg.n; i++) {
    const s = Math.round(cfg.size[0] + R() * (cfg.size[1] - cfg.size[0])), t = cfg.dur[0] + R() * (cfg.dur[1] - cfg.dur[0]);
    const p = document.createElement('i'); p.className = 'fx-p' + (R() < .5 ? ' r' : '');
    p.style.cssText = `left:${f1(R() * 104 - 2)}%;width:${s}px;height:${s}px;animation-duration:${f1(t)}s;animation-delay:${f1(-R() * t)}s`;
    const b = document.createElement('b'); b.style.cssText = `animation-duration:${f1(2.4 + R() * 2.4)}s;animation-delay:${f1(-R() * 3)}s`;
    p.appendChild(b); fx.appendChild(p);
  }
  document.body.appendChild(fx);
}
/** The snow edge with icicles along the top of the start banner (one 420 px piece that repeats). */
let ICICLES = null;
function icicles() {
  const R = seeded(5); let d = 'M420 0V5', x = 420;
  while (x > 0) {
    const nx = Math.max(0, x - (10 + R() * 18)), cx = (x + nx) / 2;
    if (R() < .5 && x - nx > 8) d += `L${f1(cx + 2.4)} 6L${f1(cx)} ${f1(10 + R() * 12)}L${f1(cx - 2.4)} 6`;
    d += `Q${f1(cx)} ${f1(6 + R() * 4)} ${f1(nx)} 5`; x = nx;
  }
  return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" width="420" height="26" viewBox="0 0 420 26" preserveAspectRatio="none"><defs><linearGradient id="i" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff"/><stop offset=".28" stop-color="#F3FAFF"/><stop offset="1" stop-color="#BFE3FF" stop-opacity=".8"/></linearGradient></defs><path d="${d}H0V0Z" fill="url(#i)"/></svg>`);
}
/** A snow cap for the start button: a bumpy top and a few short icicles (one 200 px piece that repeats). */
let SNOWCAP = null;
function snowCap() {
  const R = seeded(9); let d = 'M0 6', x = 0;
  while (x < 200) { const nx = Math.min(200, x + 14 + R() * 16); d += `Q${f1((x + nx) / 2)} ${f1(R() * 2.2)} ${f1(nx)} ${nx >= 200 ? 6 : f1(3.5 + R() * 2)}`; x = nx; }
  d += 'V8';
  while (x > 0) {
    const nx = Math.max(0, x - (9 + R() * 14)), cx = (x + nx) / 2;
    if (R() < .45 && x - nx > 7) d += `L${f1(cx + 1.8)} 9L${f1(cx)} ${f1(11 + R() * 4)}L${f1(cx - 1.8)} 9`;
    d += `Q${f1(cx)} ${f1(9 + R() * 2)} ${f1(nx)} 8`; x = nx;
  }
  return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" width="200" height="16" viewBox="0 0 200 16" preserveAspectRatio="none"><defs><linearGradient id="i" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff"/><stop offset=".5" stop-color="#F3FAFF"/><stop offset="1" stop-color="#C6E6FF"/></linearGradient></defs><path d="${d}Z" fill="url(#i)"/></svg>`);
}
/** The anime start banner: an original painted dusk over a city, with clouds lit from below, power lines and a
 *  sakura branch. Drawn once as a picture, so it costs no more than a photo. */
let ANIME_BG = null;
function animeScene() {
  const R = seeded(7), W = 1600, H = 900; let s = '';
  s += `<defs>
<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#120A30"/><stop offset=".28" stop-color="#34195A"/><stop offset=".5" stop-color="#8E3B84"/><stop offset=".64" stop-color="#E2679A"/><stop offset=".76" stop-color="#FF9F8E"/><stop offset=".86" stop-color="#FFD3A4"/></linearGradient>
<radialGradient id="sun"><stop offset="0" stop-color="#FFF8E2"/><stop offset=".6" stop-color="#FFE4B6"/><stop offset="1" stop-color="#FFBFA6"/></radialGradient>
<radialGradient id="glow"><stop offset="0" stop-color="#FFE7BE" stop-opacity=".8"/><stop offset=".35" stop-color="#FFA2B6" stop-opacity=".32"/><stop offset="1" stop-color="#FF7AA8" stop-opacity="0"/></radialGradient>
<linearGradient id="cl" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#5E3684"/><stop offset=".55" stop-color="#B95A98"/><stop offset=".85" stop-color="#FF9FA6"/><stop offset="1" stop-color="#FFC9A8"/></linearGradient>
<linearGradient id="far" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8C4A86"/><stop offset="1" stop-color="#5A2C66"/></linearGradient>
<linearGradient id="near" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2E163F"/><stop offset="1" stop-color="#140A1F"/></linearGradient>
<linearGradient id="trail" x1="0" x2="1"><stop offset="0" stop-color="#fff" stop-opacity="0"/><stop offset="1" stop-color="#fff" stop-opacity=".85"/></linearGradient>
<g id="fl"><g fill="#FFC6DD"><ellipse cx="0" cy="-5" rx="3.6" ry="5"/><ellipse cx="0" cy="-5" rx="3.6" ry="5" transform="rotate(72)"/><ellipse cx="0" cy="-5" rx="3.6" ry="5" transform="rotate(144)"/><ellipse cx="0" cy="-5" rx="3.6" ry="5" transform="rotate(216)"/><ellipse cx="0" cy="-5" rx="3.6" ry="5" transform="rotate(288)"/></g><circle r="2.2" fill="#FF6FA8"/></g>
</defs>`;
  s += `<rect width="${W}" height="${H}" fill="url(#sky)"/>`;
  for (let i = 0; i < 110; i++) { const y = R() * 360; s += `<circle cx="${(R() * W).toFixed(0)}" cy="${y.toFixed(0)}" r="${f1(.6 + R() * 1.3)}" fill="#fff" opacity="${(.2 + R() * .65 * (1 - y / 420)).toFixed(2)}"/>`; }
  s += `<path d="M160 300L820 170" stroke="url(#trail)" stroke-width="2.6" stroke-linecap="round" opacity=".55"/>`;
  s += `<circle cx="1130" cy="650" r="460" fill="url(#glow)"/><circle cx="1130" cy="650" r="118" fill="url(#sun)"/>`;
  const cloud = (cx, cy, w, k, o = .93) => {
    let c = '';
    for (let i = 0; i < k; i++) { const rx = 38 + R() * 70; c += `<ellipse cx="${(cx + (R() - .5) * w).toFixed(0)}" cy="${(cy + (R() - .5) * 28).toFixed(0)}" rx="${rx.toFixed(0)}" ry="${(rx * (.42 + R() * .2)).toFixed(0)}"/>`; }
    return `<g fill="url(#cl)" opacity="${o}">${c}</g>`;
  };
  s += cloud(380, 420, 460, 10) + cloud(1390, 360, 400, 9) + cloud(860, 505, 320, 7, .85) + cloud(120, 560, 260, 6, .8);
  const city = (base, h0, h1, fill, w0, w1, lights) => {
    let c = '', l = '', x = -20;
    while (x < W + 20) {
      const w = w0 + R() * (w1 - w0), h = h0 + R() * (h1 - h0), top = base - h;
      c += `<rect x="${x.toFixed(0)}" y="${top.toFixed(0)}" width="${(w + 1).toFixed(0)}" height="${(H - top).toFixed(0)}"/>`;
      if (R() < .18) c += `<rect x="${(x + w / 2 - 1).toFixed(0)}" y="${(top - 18 - R() * 22).toFixed(0)}" width="2" height="40"/>`;
      if (lights) for (let yy = top + 10; yy < base - 8; yy += 14) for (let xx = x + 6; xx < x + w - 8; xx += 11) if (R() < .16) l += `<rect x="${xx.toFixed(0)}" y="${yy.toFixed(0)}" width="4" height="6"/>`;
      x += w;
    }
    return `<g fill="${fill}">${c}</g>` + (l ? `<g fill="#FFD48C" opacity=".75">${l}</g>` : '');
  };
  s += city(720, 70, 210, 'url(#far)', 40, 90, false);
  s += city(775, 50, 175, 'url(#near)', 50, 110, true);
  s += `<rect y="772" width="${W}" height="${H - 772}" fill="#120819"/>`;
  // a utility pole with its wires across the sky
  s += `<g fill="#170A23"><rect x="322" y="236" width="14" height="${H - 236}"/><rect x="250" y="262" width="158" height="9" rx="2"/><rect x="268" y="304" width="122" height="8" rx="2"/>
<rect x="258" y="250" width="6" height="14"/><rect x="394" y="250" width="6" height="14"/><rect x="276" y="293" width="6" height="12"/><rect x="376" y="293" width="6" height="12"/></g>
<g fill="none" stroke="#1C0D2A" stroke-width="2.2"><path d="M397 252Q1000 420 1610 262"/><path d="M261 252Q100 330 -10 300"/><path d="M379 294Q1000 470 1610 330"/><path d="M279 294Q120 360 -10 350"/><path d="M329 238Q950 330 1610 168"/></g>`;
  // the sakura branch reaching in from the right
  s += `<g fill="none" stroke="#2A1322" stroke-linecap="round"><path d="M1640 330Q1500 360 1400 430T1215 470" stroke-width="11"/><path d="M1470 382Q1440 330 1385 318" stroke-width="6"/><path d="M1335 446Q1310 500 1270 520" stroke-width="5"/><path d="M1560 345Q1560 290 1590 255" stroke-width="5"/></g>`;
  const along = [[1600, 338], [1560, 348], [1520, 360], [1480, 378], [1450, 395], [1420, 415], [1390, 432], [1360, 444], [1330, 452], [1300, 459], [1265, 465], [1230, 470], [1455, 360], [1420, 330], [1390, 320], [1320, 480], [1290, 505], [1275, 518], [1565, 300], [1585, 265]];
  let fl = '', soft = '';
  for (const [x, y] of along) for (let j = 0; j < 3; j++) {
    const fx = x + (R() - .5) * 46, fy = y + (R() - .5) * 34, sc = .8 + R() * .9;
    fl += `<use href="#fl" transform="translate(${f1(fx)} ${f1(fy)}) rotate(${(R() * 72).toFixed(0)}) scale(${sc.toFixed(2)})"/>`;
    soft += `<circle cx="${f1(fx + (R() - .5) * 20)}" cy="${f1(fy + (R() - .5) * 16)}" r="${f1(5 + R() * 7)}"/>`;
  }
  s += `<g fill="#FF9CC6" opacity=".55">${soft}</g>${fl}`;
  // a few petals on their way down
  for (let i = 0; i < 22; i++) { const x = (900 + R() * 700).toFixed(0), y = (380 + R() * 380).toFixed(0); s += `<ellipse cx="${x}" cy="${y}" rx="3.2" ry="2" fill="#FFC2DA" opacity="${(.5 + R() * .4).toFixed(2)}" transform="rotate(${(R() * 180).toFixed(0)} ${x} ${y})"/>`; }
  return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">${s}</svg>`);
}

/* ======================= MOTION ======================= */
/** The page rests while FiveM is in front (html.game, effect layers removed) or another window is in use: no
 *  animation runs, no blur is drawn and numbers jump instead of counting, so WebView2 produces no frames at all. */
const REST = { game: false, away: false };
const resting = () => REST.game || REST.away || document.hidden;
function restSync() {
  const root = document.documentElement, rest = REST.game || REST.away;
  root.classList.toggle('rest', rest);
  root.classList.toggle('game', REST.game);
  // only the endless decorations pause. Toasts, dialogs and other short animations still play, so a message that
  // arrives meanwhile is shown (pausing everything froze them at their invisible first frame)
  for (const a of document.getAnimations()) {
    if (!a.effect || a.effect.getTiming().iterations !== Infinity) continue;
    if (rest && a.playState === 'running') { a.pause(); a._rest = true; }
    else if (!rest && a._rest) { a._rest = false; a.play(); }
  }
}
function countTo(el, to, suffix = '', dur = 700) {
  if (!el) return;
  const from = el._v ?? 0; el._v = to;
  cancelAnimationFrame(el._raf);
  // small steps (the live CPU/RAM values every second) and a resting page: set at once, counting would mean
  // drawing 40 frames a second just for a number
  if (from === to || Math.abs(to - from) <= 3 || resting()) { el.innerHTML = Math.round(to) + suffix; return; }
  const t0 = performance.now();
  const step = now => {
    const k = Math.min(1, (now - t0) / dur), e = 1 - Math.pow(1 - k, 3);
    el.innerHTML = Math.round(from + (to - from) * e) + suffix;
    if (k < 1) el._raf = requestAnimationFrame(step);
  };
  el._raf = requestAnimationFrame(step);
}
function moveInd() {
  const a = $('.nav.active'), ind = $('#navInd'); if (!a || !ind) return;
  if (a.classList.contains('mini')) { ind.style.opacity = 0; return; }   // the small buttons at the bottom have their own highlight
  ind.style.transform = `translateY(${a.offsetTop}px)`; ind.style.opacity = 1;
}
function enterPage(el) {
  el.classList.remove('enter'); void el.offsetWidth;
  // only what is shown counts for the stagger (cards in hidden tabs would push the visible ones back)
  [...el.querySelectorAll('.card,.tabs,.addbar,.savebar,.empty')].filter(c => c.offsetParent).forEach((c, i) => c.style.setProperty('--i', Math.min(i, 12)));
  el.classList.add('enter');
  clearTimeout(el._et); el._et = setTimeout(() => el.classList.remove('enter'), 1400);
}
let spotRaf = 0;
document.addEventListener('pointermove', e => {
  if (spotRaf) return;
  spotRaf = requestAnimationFrame(() => {
    spotRaf = 0;
    const c = e.target.closest && e.target.closest('.card'); if (!c) return;
    const r = c.getBoundingClientRect();
    c.style.setProperty('--mx', (e.clientX - r.left) + 'px'); c.style.setProperty('--my', (e.clientY - r.top) + 'px');
  });
});
function smoothPath(pts) {
  let d = `M${pts[0][0].toFixed(1)},${pts[0][1].toFixed(1)}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] || pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] || p2;
    const c1 = [p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6], c2 = [p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6];
    d += ` C${c1.map(v => v.toFixed(1))} ${c2.map(v => v.toFixed(1))} ${p2.map(v => v.toFixed(1))}`;
  }
  return d;
}

/* ======================= NAV ======================= */
let curPage = 'home';
const NAV_OF = { display: 'gfx', minimap: 'mods' };
function go(p) {
  if (p === 'minimap') { LIB.cat = 'minimap'; p = 'mods'; }
  if ((p === 'display') !== (curPage === 'display')) api('display.preview', { on: p === 'display' }).catch(() => { });
  if ((p === 'pad') !== (curPage === 'pad')) api('pad.watch', { on: p === 'pad' }).then(r => r && padState(r.state, r.active)).catch(() => { });
  if (p !== 'mods' && MMO.prev) mmoPreview(false);
  curPage = p; document.documentElement.dataset.page = p;
  $$('.nav').forEach(n => n.classList.toggle('active', n.dataset.p === (NAV_OF[p] || p)));
  $$('.page').forEach(s => s.classList.toggle('show', s.id === 'p-' + p));
  moveInd(); enterPage($('#p-' + p));
  ({ servers: () => S.servers.length || loadServers(), gfx: () => { S.gfx || loadGfx(); gwRefresh(); }, f8: () => loadF8().then(renderF8), admin: () => admShow(admTab), minimap: modsRefresh, clip: () => { clipList(); hlLoad(); }, mods: modsRefresh, settings: () => { setTab($('#setTabs .tab.on')?.dataset.st || 'account'); spProfile(); }, xhair: () => setTimeout(xhPlace, 30), cleaner: () => S.clean.length || scan(),
     diag: () => { S.health ? renderHealth(S.health) : runHealth(); loadLogs(); }, launcher: renderCmd, home: refreshCache })[p]?.();
}
window.go = go;

/* ======================= TOAST / FEED ======================= */
function toast(title, text = '', ok = true) {
  const t = document.createElement('div');
  t.className = 'toast' + (ok ? '' : ' err');
  t.innerHTML = `<div class="ti">${ico(ok ? 'check' : 'alert')}</div><div><b>${esc(title)}</b>${text ? `<span>${esc(text)}</span>` : ''}</div>`;
  $('#toasts').append(t);
  setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 250); }, 3600);
}
function feed(text) {
  const f = $('#feed'), d = document.createElement('div');
  d.innerHTML = `<em>${new Date().toLocaleTimeString('de-DE')}</em>  ${esc(text)}`;
  f.prepend(d); while (f.children.length > 6) f.lastChild.remove();
}

/* ======================= EVENTS ======================= */
function onEvent(ev, d) {
  if (ev === 'stats') stats(d);
  else if (ev === 'log') feed(d.text);
  else if (ev === 'boost') boostProgress(d.pct, d.text);
  else if (ev === 'update') updProgress(d);
  else if (ev === 'xhair') { XH.on = d.on; xhState(); toast(d.on ? 'Fadenkreuz an' : 'Fadenkreuz aus', d.on ? 'per Taste · Server-Regeln beachten' : 'per Taste'); }
  else if (ev === 'display') { DP.cfg.on = d.on; dpState(); }
  else if (ev === 'locked') gate(d && d.reason === 'expired' ? 'Dein Key ist abgelaufen. Mit einem neuen Key geht es sofort weiter.' : 'Dein Key wurde gesperrt oder ist auf einem anderen PC aktiv.');
  else if (ev === 'pad') padState(d);
  else if (ev === 'padon') { PAD.cfg.mode = d.mode; padUI(); toast(d.mode === 'off' ? 'Controller-Anzeige aus' : 'Controller-Anzeige automatisch', 'per Taste'); }
  else if (ev === 'modInstalled') { modReport({ notes: d.notes, off: d.off, mods: [{ name: d.name, cat: d.cat }] }); modList(); }
  else if (ev === 'reshade') { for (const k in LIB.busy) LIB.busy[k] = d.pct; libRender(); }
  else if (ev === 'modget') { if (d.id in LIB.busy) { LIB.busy[d.id] = d.pct; libRender(); } }
  else if (ev === 'dlNew') {   // a pack landed in the Downloads folder (saved from Discord, for example)
    toast('Neues Pack in deinen Downloads', `${d.name} · unter Mods → Dateien mit einem Klick installieren`);
    if (curPage === 'mods') dlList('#modDl');
  }
  else if (ev === 'gamefront') { REST.game = !!d.on; restSync(); }   // FiveM is in front: nothing moves, effect layers gone
  else if (ev === 'winfocus') { REST.away = !d.on; restSync(); }     // another program is being used: everything pauses
  else if (ev === 'clipwords') { CL.words = d.words; if ($('#clWords') && document.activeElement !== $('#clWords')) $('#clWords').value = CL.words; }
  else if (ev === 'kill') { feed(d.kill ? '🎯 Kill erkannt, Clip folgt' : '💀 Tod erkannt, Clip folgt'); }
  else if (ev === 'clip') {
    if (d.error) toast('Kein Clip gespeichert', d.error, false);
    else if (d.recorder === 'orbit') toast(d.auto ? 'Auto-Clip gespeichert' : 'Clip gespeichert', d.file || '');
    else toast(d.auto ? 'Auto-Clip gespeichert' : 'Clip gespeichert', d.auto ? 'Nach einem Gefecht · ' + (CL_RECS[d.recorder]?.[0] || '') : 'per Taste · ' + (CL_RECS[d.recorder]?.[1] || ''));
    if (curPage === 'clip') setTimeout(clipList, d.recorder === 'orbit' ? 600 : 4000);
  }
  else if (ev === 'mmbuild') mmbProgress(d);
  else if (ev === 'highlight' || ev === 'share') hlProgress(d);
  else if (ev === 'highlightDone') {   // made by itself after the game was closed
    $('#hlProg')?.classList.add('hidden');
    if (d.file) passed('Highlight-Video der Runde fertig', `${d.clips} Clips · ${d.file}`); else toast('Kein Highlight-Video', d.error || '', false);
    if (curPage === 'clip') { clipList(); hlLoad(); }
  }
  else if (ev === 'lag') { if (wasdSt && wasdSt.chop) { wasdSt.chop.on = d.on; wasdUI(); } }   // switched with the key in the game
}
/** A live value that changes every second: set at once (counting up to it would redraw the page for most of every second). */
function setNum(el, v, suffix) { if (!el || el._v === v) return; el._v = v; el.innerHTML = Math.round(v) + suffix; }
function stats(d) {
  if (!S.cpuHist.length) S.cpuHist = Array(39).fill(d.cpu);   // the curve starts flat at the first real value
  S.cpuHist.push(d.cpu); if (S.cpuHist.length > 40) S.cpuHist.shift();
  // (also while FiveM is in front: OrbitTweak may be open on a second screen; a few numbers once a second cost nothing)
  $('#tbCpu').textContent = d.cpu + '%'; $('#tbRam').textContent = d.ram + '%';
  setNum($('#mCpu'), d.cpu, '<small>%</small>');
  setNum($('#mRam'), d.ram, '<small>%</small>');
  $('#mRamTxt').textContent = `${nf1.format(d.ramUsed)} / ${d.ramTotal.toFixed(0)} GB`;
  setNum($('#cRam'), d.ram, '<small>%</small>'); $('#cRamBar').style.width = d.ram + '%';
  $('#cRamTxt').textContent = `${nf1.format(d.ramUsed)} / ${d.ramTotal.toFixed(0)} GB`;
  const n = S.cpuHist.length, pts = S.cpuHist.map((v, i) => [i * 200 / (n - 1), 58 - v / 100 * 52]);
  const line = smoothPath(pts);
  $('#sparkL').setAttribute('d', line);
  $('#sparkF').setAttribute('d', line + ' L200,60 L0,60 Z');
  fmState(d.fm);
}
function fmState(f) {
  const on = f.running;
  $('#tbDot').classList.toggle('on', on);
  $('#tbFm').textContent = f.game ? 'FiveM · im Spiel' : on ? 'FiveM · Launcher' : 'FiveM offline';
  $('#fmIco').classList.toggle('off', !on);
  $('#fmState').textContent = f.game ? 'Im Spiel' : on ? 'Launcher offen' : 'FiveM offline';
  const chip = $('#fmChip');
  chip.className = 'chip ' + (f.game ? 'g' : on ? 'w' : '');
  chip.textContent = f.game ? 'Live' : on ? 'Lädt' : 'Standby';
  $('#fmBuild').textContent = f.build || '—';
  $('#fmPrio').textContent = f.prio || '—';
  $('#fmRam').textContent = f.game ? bytes(f.gameRam) : '—';
  // while the game runs there is nothing to start: one way out instead, and the big start buttons calm down
  if (S.fmOn === on && S.fmGame === !!f.game) return;
  S.fmOn = on; S.fmGame = !!f.game;
  $('#fmStart')?.classList.toggle('hidden', on); $('#fmStop')?.classList.toggle('hidden', !on);
  $('#sideLaunch')?.classList.toggle('calm', on);
  if ($('#launchTxt')) $('#launchTxt').textContent = on ? 'FiveM läuft' : 'FiveM starten';
  if ($('#launchSub')) $('#launchSub').textContent = on ? (f.game ? 'Im Spiel · zur Übersicht' : 'Launcher offen · zur Übersicht') : S.fivem && S.fivem.found === false ? 'FiveM nicht gefunden' : 'mit Pre-Launch-Boost';
  lhState();
}
/** Side bar button: starts FiveM, or shows the start page while it already runs. */
function sideLaunch() { if (S.fmOn) return go('home'); launch(''); }
window.sideLaunch = sideLaunch;
/** The big tile on the launcher page: start, or connect the running game to the address typed in. */
function lhState() {
  const b = $('#lhGo'); if (!b) return;
  const con = ($('#lConnect')?.value || '').trim();
  b.classList.toggle('calm', !!S.fmOn && !con);
  $('#lhTxt').textContent = S.fmOn ? (con ? 'Verbinden' : 'FiveM läuft') : 'FiveM starten';
  $('#lhSub').textContent = S.fmOn ? (con ? 'mit ' + con : 'Adresse eintragen, um zu verbinden') : con ? 'Boost anwenden und verbinden' : 'Boost anwenden und Spiel starten';
  const chip = $('#lhChip');
  if (chip) {
    const st = S.fmOn ? ['g live', 'FiveM läuft'] : S.fivem && S.fivem.found === false ? ['r', 'FiveM nicht gefunden'] : ['', 'Bereit zum Start'];
    chip.className = 'chip ' + st[0]; chip.textContent = st[1];
  }
}
function lhLaunch() {
  if (S.fmOn && !($('#lConnect')?.value || '').trim()) return toast('FiveM läuft schon', 'Trag eine Adresse ein, um direkt zu verbinden');
  launch();
}
window.lhLaunch = lhLaunch;
async function ping() {
  try {
    const p = await api('ping');
    $('#tbPing').textContent = p.ms ?? '--';
    $('#mPing').innerHTML = `${p.ms ?? '--'}<small>ms</small>`;
    $('#mPingTxt').textContent = p.ms == null ? 'Keine Verbindung' : `Cfx.re · ${p.ms < 40 ? 'sehr gut' : p.ms < 80 ? 'gut' : 'hoch'}`;
  } catch { }
}
async function refreshCache() { try { const r = await api('fivem.cacheSize'); $('#fmCache').textContent = r.found ? bytes(r.size) : '—'; } catch { } }

/* ======================= SCORE / BOOST ======================= */
function score() {
  const t = S.tweaks, on = t.filter(x => x.applied).length, pct = t.length ? Math.round(on * 100 / t.length) : 0;
  $('#hsTweaks').textContent = `${on}/${t.length}`;
  if (!S.boosting) { setRing(pct); $('#scoreChip').textContent = pct >= 90 ? 'Optimal' : 'Bereit'; $('#scoreChip').className = 'chip ' + (pct >= 90 ? 'g' : ''); }
}
function setRing(p) {
  p = Math.max(0, Math.min(p, 100));
  $('#scoreRing').style.strokeDashoffset = 678.6 * (1 - p / 100);
  countTo($('#ringNum'), p, '<small>%</small>', 1200);
}
function addFreed(b) { S.freed += b || 0; $('#hsFreed').textContent = bytes(S.freed); }
function boostProgress(pct, text) {
  setRing(pct); $('#heroText').textContent = text;
  const w = $('#wizProg'); if (w) { w.style.width = pct + '%'; $('#wizTxt').textContent = text; }
}
async function boost(fromWizard) {
  if (S.boosting) return;
  S.boosting = true;
  const btn = $('#boostBtn'), g = $('#gauge');
  btn.classList.remove('done'); btn.classList.add('run'); g.classList.add('run');
  $('#scoreChip').textContent = 'Läuft'; $('#scoreChip').className = 'chip a';
  $('#heroTitle').textContent = 'Boost läuft…'; $('#boostSub').textContent = 'wird optimiert';
  $('#boostTxt').textContent = 'Optimiere…';
  $('#boostIco').innerHTML = '<use href="#refresh"/>'; $('#boostIco').classList.add('spin');
  setRing(0);
  feed('Boost gestartet');
  try {
    const r = await api('boost');
    addFreed(r.freed); await loadTweaks(); setRing(100);
    g.classList.remove('pop'); void g.offsetWidth; g.classList.add('pop');
    btn.classList.add('done'); $('#boostTxt').textContent = 'Fertig'; $('#boostSub').textContent = 'optimiert';
    $('#boostIco').classList.remove('spin'); $('#boostIco').innerHTML = '<use href="#check"/>';
    $('#scoreChip').textContent = 'Fertig'; $('#scoreChip').className = 'chip g';
    $('#heroTitle').textContent = 'Boost abgeschlossen';
    $('#heroText').textContent = r.restart ? 'Fertig. Starte den PC neu, damit alle Tweaks greifen.' : 'Fertig. Dein System ist bereit für FiveM.';
    $('#lastBoost').textContent = 'Letzter Boost: gerade eben'; $('#lastBoost').classList.remove('hidden');
    if (!fromWizard) passed('Boost abgeschlossen', `${r.applied} Tweaks · ${bytes(r.freed)} frei${r.restart ? ' · Neustart empfohlen' : ''}`);
    if (!fromWizard) toast('Boost abgeschlossen', `${r.applied} Tweaks · ${bytes(r.freed)} freigegeben${r.restart ? ' · Neustart empfohlen' : ''}`);
    feed(`Boost fertig: ${r.applied} Tweaks, ${bytes(r.freed)} frei`);
    return r;
  } catch (e) { toast('Boost fehlgeschlagen', e.message, false); }
  finally {
    btn.classList.remove('run'); g.classList.remove('run');
    $('#boostIco').classList.remove('spin');
    S.boosting = false;
    setTimeout(() => {
      if (S.boosting) return;
      btn.classList.remove('done'); $('#boostTxt').textContent = 'Boost starten'; $('#boostIco').innerHTML = '<use href="#zap"/>';
      $('#heroTitle').textContent = 'Kompletter FiveM-Boost'; $('#boostSub').textContent = 'optimiert'; score();
    }, 2800);
  }
}

/* ======================= TWEAKS ======================= */
const CATS = { fivem: ['FiveM', 'gamepad', 'Spiel-Priorität, GPU und Frametimes'], system: ['System', 'cpu', 'Windows-Einstellungen für weniger Ruckler'], network: ['Netzwerk & Hitreg', 'wifi', 'Niedrigere Latenz, stabilere Treffer'], input: ['Input', 'keyboard', 'Maus und Tastatur direkter'] };
let curTab = 'fivem';
async function loadTweaks() { S.tweaks = await api('tweaks.list'); renderTweaks(); score(); }
function tweakHtml(t, compact) {
  const badge = !t.badge ? '' : t.badge === 'NEUSTART' ? '<span class="eff w">Neustart</span>' : t.badge === 'LIVE' ? '<span class="eff a" title="Wirkt sofort, solange OrbitTweak läuft">Live</span>' : `<span class="eff">${esc(t.badge)}</span>`;
  // most tweaks are recommended, so only the exceptions are marked
  const rec = !compact && !t.rec ? '<span class="eff">Optional</span>' : '';
  const body = `<div class="ti">${ico(t.icon)}</div><div class="tx"><b>${esc(t.name)}</b>${badge}${rec}<p>${esc(t.desc)}</p></div><div class="tg ${t.applied ? 'on' : ''}" data-id="${t.id}"></div>`;
  return `<div class="tw ${t.applied ? 'on' : ''}">${body}</div>`;
}
function renderTweaks() {
  // each category: its name, how many of its tweaks are on as a large number, and a fine bar for the same
  $('#tabs').innerHTML = Object.entries(CATS).map(([k, [n, i]]) => {
    const l = S.tweaks.filter(t => t.cat === k), on = l.filter(t => t.applied).length;
    return `<button class="tab ${k === curTab ? 'on' : ''}" data-c="${k}"><span class="tc-h">${ico(i)}${n}</span><span class="tc-n"><b>${on}</b>von ${l.length} aktiv</span><span class="tc-bar"><i style="transform:scaleX(${l.length ? on / l.length : 0})"></i></span></button>`;
  }).join('');
  const cur = S.tweaks.filter(t => t.cat === curTab);
  $('#tlDesc').textContent = CATS[curTab][2];
  $('#tgrid').innerHTML = cur.map(t => tweakHtml(t)).join('');
  const inp = S.tweaks.filter(t => t.cat === 'input');
  $('#inputTweaks').innerHTML = inp.map(t => tweakHtml(t, true)).join('');
  $('#inputTwCount').textContent = `${inp.filter(t => t.applied).length}/${inp.length} aktiv`;
}
document.addEventListener('click', async e => {
  const tab = e.target.closest('#tabs .tab'); if (tab) { curTab = tab.dataset.c; renderTweaks(); return; }
  if (e.target.closest('.tab')) return;
  const tg = e.target.closest('.tg[data-id]') || e.target.closest('.tw')?.querySelector('.tg[data-id]'); if (!tg) return;
  const t = S.tweaks.find(x => x.id === tg.dataset.id); if (!t) return;
  const want = !t.applied;
  $$(`.tg[data-id="${t.id}"]`).forEach(x => { x.classList.toggle('on', want); x.classList.add('busy'); });
  try {
    const r = await api('tweak.set', { id: t.id, on: want }); t.applied = r.applied;
    if (r.error) toast('Fehlgeschlagen', `${t.name}: ${r.error}`, false);
    else toast(want ? 'Aktiviert' : 'Zurückgesetzt', t.name + (t.badge === 'NEUSTART' && want ? ' · Neustart nötig' : ''));
  } catch (err) { toast('Fehler', err.message, false); }
  renderTweaks(); score();
});
async function applyCat(cat) {
  const list = S.tweaks.filter(t => t.cat === cat && t.rec && !t.applied);
  if (!list.length) return toast('Schon optimal', `Alle empfohlenen ${CATS[cat][0]}-Tweaks sind aktiv`);
  for (const t of list) { try { const r = await api('tweak.set', { id: t.id, on: true }); t.applied = r.applied; } catch { } renderTweaks(); }
  score(); toast(`${CATS[cat][0]} optimiert`, `${list.length} Tweaks angewendet`);
}
async function revertAll() {
  if (!await confirmBox('Alle Tweaks zurücksetzen?', 'Windows, Netzwerk, Input und FiveM werden auf Standard zurückgesetzt.', { ok: 'Alles zurücksetzen', danger: true })) return;
  await api('tweaks.revertAll'); await loadTweaks(); toast('Alles zurückgesetzt', 'Neustart empfohlen');
}

/* ======================= FIVEM ACTIONS ======================= */
async function clearFmCache() { const r = await api('fivem.cache'); if (r.error) return toast('Cache nicht geleert', r.error, false); addFreed(r.freed); refreshCache(); toast('FiveM-Cache geleert', bytes(r.freed) + ' freigegeben'); }
async function cleanRam() { const r = await api('ram.clean'); addFreed(r.freed); if ($('#cFreed')) $('#cFreed').textContent = bytes(S.freed); toast('RAM freigegeben', bytes(r.freed) + ' zurückgewonnen'); }
async function killFiveM() { if (!await confirmBox('FiveM beenden?', 'Das Spiel wird sofort geschlossen. Was nicht gespeichert ist, geht verloren.', { ok: 'FiveM beenden', danger: true })) return; const r = await api('fivem.kill'); toast(r.count ? 'FiveM beendet' : 'FiveM lief nicht', r.count ? r.count + ' Prozesse geschlossen' : '', !!r.count); }
async function pickFiveM() { const r = await api('fivem.pick'); if (r.found) { S.fivem = r; renderCmd(); refreshCache(); toast('FiveM-Pfad gesetzt', r.path); } }

/* ======================= LAUNCHER ======================= */
const BUILDS = ['Auto', '2189', '2372', '2545', '2612', '2699', '2802', '2944', '3095', '3258', '3323', '3407'];
const PRE = [['ram', 'RAM freigeben', 'Working-Sets + Standby-Liste leeren', 'ram'], ['dns', 'DNS-Cache leeren', 'Frische Server-Auflösung', 'globe'], ['kill', 'Hintergrund-Apps beenden', 'OneDrive, Teams, Launcher & Co.', 'power'], ['cache', 'FiveM-Cache leeren', 'Nur bei Problemen (längeres Laden)', 'trash'], ['prio', 'Priorität „Hoch“ setzen', 'Automatisch, sobald das Spiel läuft', 'gauge']];
const cfg = () => S.settings.launch ||= { build: 'Auto', pure: 0, pre: { ram: true, dns: true, kill: false, cache: false, prio: true } };
function seg(el, opts, cur, onPick) {
  el.innerHTML = opts.map(([n, v]) => `<button class="${String(v) === String(cur) ? 'on' : ''}" data-v="${v}">${n}</button>`).join('');
  el.onclick = e => { const b = e.target.closest('button'); if (!b) return; el.querySelectorAll('button').forEach(x => x.classList.toggle('on', x === b)); onPick(b.dataset.v); };
}
async function renderLauncher() {
  const c = cfg();
  const sb = $('#optBuild'); sb.innerHTML = BUILDS.map(b => `<option value="${b}" ${b === c.build ? 'selected' : ''}>${b === 'Auto' ? 'Automatisch (empfohlen)' : 'Build ' + b}</option>`).join('');
  sb.onchange = () => { c.build = sb.value; saveLaunch(); };
  seg($('#optPure'), [['Aus', 0], ['Pure 1', 1], ['Pure 2', 2]], c.pure, v => { c.pure = +v; saveLaunch(); });
  try { const ini = await api('ini.get'); S.chan = ini.channel; seg($('#optChan'), [['Release', 'production'], ['Beta', 'beta'], ['Latest', 'canary']], ini.channel, async v => { S.chan = v; renderCmd(); await api('ini.set', { channel: v }); toast('Update-Kanal gesetzt', v); }); } catch { }
  $('#preList').innerHTML = PRE.map(([k, n, d, i]) => `<div class="pr ${c.pre[k] ? 'on' : ''}"><span class="pi2">${ico(i)}</span><div><b>${n}</b><span>${d}</span></div><div class="tg ${c.pre[k] ? 'on' : ''}" data-pre="${k}"></div></div>`).join('');
  $('#preList').onclick = e => { const t = e.target.closest('[data-pre]'); if (!t) return; c.pre[t.dataset.pre] = !c.pre[t.dataset.pre]; t.classList.toggle('on'); t.closest('.pr').classList.toggle('on'); saveLaunch(); };
  renderCmd();
}
function saveLaunch() { api('settings.set', { key: 'launch', value: cfg() }); renderCmd(); }
function renderCmd() {
  const c = cfg(), con = ($('#lConnect')?.value || '').trim();
  let s = `<span class="ca">"${esc((S.fivem && S.fivem.path) || '%LocalAppData%\\FiveM')}\\FiveM.exe"</span>`;
  if (c.build !== 'Auto') s += ` <em>-b${esc(c.build)}</em>`;
  if (+c.pure) s += ` <em>-pure_${c.pure}</em>`;
  if (con) s += ` <em>+connect ${esc(con)}</em>`;
  $('#cmdLine').innerHTML = s;
  const on = PRE.filter(([k]) => c.pre[k]);
  if ($('#preCount')) $('#preCount').textContent = on.length + ' von ' + PRE.length + ' aktiv';
  const chan = { production: 'Release', beta: 'Beta', canary: 'Latest' }[S.chan] || 'Release';
  // each tile leads to the option it sums up
  if ($('#lSum')) $('#lSum').innerHTML = [
    ['layers', 'Build', c.build === 'Auto' ? 'Automatisch' : c.build, 'loBuild'],
    ['shield', 'Pure Mode', +c.pure ? 'Pure ' + c.pure : 'Aus', 'loPure'],
    ['download', 'Kanal', chan, 'loChan'],
    ['sparkle', 'Boost', on.length ? on.length + ' Schritte' : 'Aus', 'preCard'],
    ['link', 'Server', con ? con : 'Keiner', 'lConnect']
  ].map(([i, k, v, to]) => `<button class="lh-i${to === 'lConnect' && !con ? ' dim' : ''}" data-lgo="${to}" title="${to === 'lConnect' ? (con ? esc(con) : 'Ohne Adresse öffnet FiveM die Serverliste') : 'Zur Einstellung'}">${ico(i)}<span>${k}</span><b>${esc(v)}</b></button>`).join('');
  lhState();
}
document.addEventListener('click', e => {
  const t = e.target.closest('[data-lgo]'); if (!t) return;
  const el = $('#' + t.dataset.lgo); if (!el) return;
  if (el.tagName === 'INPUT') { el.focus(); return; }
  el.scrollIntoView({ behavior: 'smooth', block: 'center' });
  el.classList.remove('flash'); void el.offsetWidth; el.classList.add('flash');
});
async function launch(connect) {
  const c = cfg();
  const r = await api('fivem.launch', { build: c.build, pure: c.pure, connect: connect ?? ($('#lConnect').value || '').trim(), pre: c.pre });
  if (r.error) return toast('Start fehlgeschlagen', r.error, false);
  addFreed(r.freed); toast('FiveM wird gestartet', r.summary || 'Pre-Launch-Boost angewendet');
}

/* ======================= SERVERS ======================= */
const FIVEM_COLORS = ['#fff', '#F44336', '#4CAF50', '#FFEB3B', '#42A5F5', '#03A9F4', '#9C27B0', '#fff', '#FF5722', '#9E9E9E'];
function fivemName(s) {
  let out = '', col = null;
  for (const part of String(s || '').split(/(\^\d)/)) {
    const m = part.match(/^\^(\d)$/);
    if (m) { col = FIVEM_COLORS[+m[1]]; continue; }
    if (part) out += col ? `<span style="color:${col}">${esc(part)}</span>` : esc(part);
  }
  return out || 'Unbekannter Server';
}
function srvCard(s) {
  const pct = s.max ? Math.min(100, Math.round(s.players * 100 / s.max)) : 0;
  const initial = (s.name || '?').replace(/\^\d/g, '').trim()[0] || '?';
  const pc = s.ping == null ? '' : s.ping < 50 ? 'g' : s.ping < 100 ? 'w' : 'r';
  return `<div class="srv ${s.online ? '' : 'off'}">
    <div class="icon" ${s.icon ? `style="background-image:url('${esc(s.icon)}')"` : ''}>${s.icon ? '' : esc(initial)}</div>
    <div class="sx"><div class="nm">${fivemName(s.name)}</div>
      <div class="ds">${s.online ? '' : '<span class="chip r">Offline</span>'}${s.locale ? `<span class="loc">${esc(s.locale)}</span>` : ''}<span class="dt">${esc(s.desc || s.addr || '')}</span></div></div>
    <div class="pl" title="Spieler"><b>${s.players ?? 0}<small> / ${s.max ?? 0}</small></b><i class="pb"><i style="transform:scaleX(${pct / 100})"></i></i></div>
    <div class="pg ${pc}" title="Ping"><i></i><b>${s.ping == null ? '–' : s.ping}</b><small>ms</small></div>
    <button class="btn sm go" onclick="joinServer('${esc(s.code)}')" ${s.online ? '' : 'disabled'}>${ico('play')}Verbinden</button>
    <button class="ib rm" title="Aus Favoriten entfernen" onclick="removeServer('${esc(s.code)}')">${ico('x')}</button>
  </div>`;
}
async function loadServers(force) {
  // quiet placeholder rows in the shape of the real ones, so nothing jumps when the data arrives
  $('#srvList').innerHTML = Array.from({ length: Math.max(2, Math.min(S.servers.length || 3, 5)) }, () => '<div class="srv skel"><div class="icon"></div><div class="sx"><i></i><i></i></div></div>').join('');
  $('#srvCount').textContent = '';
  try { S.servers = await api('servers.list', { force: !!force }); } catch { S.servers = []; }
  renderServers(); loadCfx();
}
function renderServers() {
  const on = S.servers.filter(x => x.online).length;
  $('#srvCount').textContent = S.servers.length ? `${on} von ${S.servers.length} online` : '';
  $('#srvList').innerHTML = S.servers.length ? S.servers.map(srvCard).join('')
    : `<div class="srv-empty"><div class="se-ic">${ico('server')}</div><b>Noch keine Favoriten</b><p>Füg oben einen Join-Code (cfx.re/join/…) oder IP:Port ein. Danach siehst du hier Spieler und Ping und verbindest mit einem Klick.</p></div>`;
}
async function addServer() {
  const q = $('#srvIn').value.trim(); if (!q) return;
  try { const s = await api('servers.add', { q }); S.servers = S.servers.filter(x => x.code !== s.code).concat(s); renderServers(); $('#srvIn').value = ''; toast('Server hinzugefügt', String(s.name).replace(/\^\d/g, '')); }
  catch (e) { toast('Server nicht gefunden', e.message, false); }
}
async function removeServer(code) { await api('servers.remove', { code }); S.servers = S.servers.filter(x => x.code !== code); renderServers(); }
function joinServer(code) { const s = S.servers.find(x => x.code === code); launch(s.connect || code); }
function directConnect() { const v = $('#dcIn').value.trim(); if (v) launch(v); }
async function loadCfx() {
  const sum = $('#cfxSum');
  if (sum) { sum.className = 'cfx-sum'; sum.innerHTML = '<i></i><span>Prüfe die Dienste…</span>'; }
  try {
    const l = await api('cfx.status'), down = l.filter(s => !s.ok).length;
    if (sum) { sum.className = 'cfx-sum ' + (down ? 'bad' : 'ok'); sum.innerHTML = `<i></i><span>${down ? (down === 1 ? 'Ein Dienst gestört' : down + ' Dienste gestört') : 'Alle Dienste erreichbar'}</span>`; }
    $('#cfxList').innerHTML = l.map(s => `<div class="svc ${s.ok ? '' : 'down'}"><i class="sd"></i><span class="nm">${esc(s.name)}</span><b>${s.ok ? `${s.ms}<small>ms</small>` : 'Gestört'}</b></div>`).join('');
  } catch { if (sum) { sum.className = 'cfx-sum bad'; sum.innerHTML = '<i></i><span>Nicht erreichbar</span>'; } $('#cfxList').innerHTML = ''; }
}

/* ======================= GRAPHICS ======================= */
const Q4 = [['Normal', 0], ['Hoch', 1], ['Sehr hoch', 2], ['Ultra', 3]], Q3 = Q4.slice(0, 3);
const GFX = [
  { k: 'Windowed', n: 'Anzeigemodus', d: 'Randlos für Fadenkreuz-Overlay', o: [['Vollbild', 0], ['Fenster', 1], ['Randlos', 2]] },
  { k: 'TextureQuality', n: 'Texturen', d: 'Abhängig vom VRAM', o: Q3 },
  { k: 'ShadowQuality', n: 'Schatten', d: 'Großer FPS-Faktor', o: [['Normal', 1], ['Hoch', 2], ['Sehr hoch', 3]] },
  { k: 'ReflectionQuality', n: 'Reflexionen', d: 'Autos und Wasser', o: Q4 },
  { k: 'GrassQuality', n: 'Gras', d: 'Sehr FPS-lastig', o: Q4 },
  { k: 'WaterQuality', n: 'Wasser', d: 'Wellen und Brechung', o: Q3 },
  { k: 'ParticleQuality', n: 'Partikel', d: 'Explosionen, Rauch', o: Q4 },
  { k: 'ShaderQuality', n: 'Shader', d: 'Beleuchtungsdetails', o: Q3 },
  { k: 'PostFX', n: 'Post-FX', d: 'Bloom, Filter, Effekte', o: Q4 },
  { k: 'SSAO', n: 'Umgebungsverdeckung', d: 'Kontaktschatten', o: [['Aus', 0], ['Normal', 1], ['Hoch', 2]] },
  { k: 'Tessellation', n: 'Tessellation', d: 'Geometrie-Details', o: [['Aus', 0], ['Normal', 1], ['Hoch', 2], ['Sehr hoch', 3]] },
  { k: 'MSAA', n: 'MSAA', d: 'Kantenglättung (teuer)', o: [['Aus', 0], ['2x', 2], ['4x', 4], ['8x', 8]] },
  { k: 'FXAA_Enabled', n: 'FXAA', d: 'Kantenglättung (günstig)', o: [['Aus', 'false'], ['An', 'true']] },
  { k: 'AnisotropicFiltering', n: 'Anisotrope Filterung', d: 'Schärfere Texturen in der Ferne', o: [['Aus', 0], ['4x', 4], ['8x', 8], ['16x', 16]] },
  { k: 'Shadow_SoftShadows', n: 'Weiche Schatten', d: 'Schattenkanten', o: [['Scharf', 0], ['Weich', 1], ['Weicher', 2], ['Max', 3]] },
  { k: 'LodScale', n: 'Distanz-Skalierung', d: 'Sichtweite von Objekten', r: 1 },
  { k: 'CityDensity', n: 'Bevölkerungsdichte', d: 'NPCs und Verkehr', r: 1 },
  { k: 'MotionBlurStrength', n: 'Bewegungsunschärfe', d: 'Für PvP auf 0', r: 1 },
];
const PRESETS = {
  comp: { n: 'Leistung', d: 'Maximale FPS und klare Sicht. Für PvP, Gangwar und schwächere PCs.', fps: 5, q: 1,
    v: { TextureQuality: 1, ShadowQuality: 1, ReflectionQuality: 0, GrassQuality: 0, WaterQuality: 0, ParticleQuality: 0, ShaderQuality: 0, PostFX: 0, SSAO: 0, Tessellation: 0, MSAA: 0, FXAA_Enabled: 'true', AnisotropicFiltering: 4, Shadow_SoftShadows: 0, LodScale: 0.6, CityDensity: 0.3, MotionBlurStrength: 0 } },
  bal: { n: 'Ausgewogen', d: 'Gute Optik bei stabilen FPS. Für Roleplay im Alltag.', fps: 3, q: 3,
    v: { TextureQuality: 2, ShadowQuality: 2, ReflectionQuality: 1, GrassQuality: 1, WaterQuality: 1, ParticleQuality: 1, ShaderQuality: 1, PostFX: 1, SSAO: 1, Tessellation: 1, MSAA: 0, FXAA_Enabled: 'true', AnisotropicFiltering: 16, Shadow_SoftShadows: 1, LodScale: 0.8, CityDensity: 0.6, MotionBlurStrength: 0 } },
  cine: { n: 'Kino', d: 'Maximale Qualität für Screenshots, Clips und High-End-PCs.', fps: 1, q: 5,
    v: { TextureQuality: 2, ShadowQuality: 3, ReflectionQuality: 3, GrassQuality: 2, WaterQuality: 2, ParticleQuality: 3, ShaderQuality: 2, PostFX: 3, SSAO: 2, Tessellation: 3, MSAA: 2, FXAA_Enabled: 'true', AnisotropicFiltering: 16, Shadow_SoftShadows: 3, LodScale: 1, CityDensity: 1, MotionBlurStrength: 0 } },
};
const PRESET_ICO = { comp: 'zap', bal: 'sliders', cine: 'film' };
const GFX_GROUPS = [['Anzeige & Welt', 'monitor', ['Windowed', 'LodScale', 'CityDensity', 'MotionBlurStrength']], ['Texturen & Kanten', 'layers', ['TextureQuality', 'AnisotropicFiltering', 'FXAA_Enabled', 'MSAA']],
  ['Licht & Schatten', 'sun', ['ShadowQuality', 'Shadow_SoftShadows', 'ReflectionQuality', 'SSAO', 'ShaderQuality', 'PostFX']], ['Details', 'sparkle', ['GrassQuality', 'WaterQuality', 'ParticleQuality', 'Tessellation']]];
const pips = n => Array.from({ length: 5 }, (_, i) => `<i class="${i < n ? 'on' : ''}"></i>`).join('');
const num = v => v === 'true' ? 1 : v === 'false' ? 0 : parseFloat(v);
async function loadGfx() {
  try { S.gfx = await api('gfx.get'); } catch { S.gfx = { found: false, values: {} }; }
  S.gfxEdit = { ...S.gfx.values };
  $('#gfxState').className = 'chip ' + (S.gfx.found ? 'g' : 'r');
  $('#gfxState').textContent = S.gfx.found ? 'settings.xml gefunden' : 'settings.xml fehlt';
  $('#gfxPath').textContent = S.gfx.found ? S.gfx.path : 'GTA V settings.xml nicht gefunden. Starte GTA V oder FiveM einmal.';
  $('#presets').innerHTML = Object.entries(PRESETS).map(([k, p]) => `<div class="card hov preset" data-pr="${k}"><div class="pr-h"><span class="pr-ic">${ico(PRESET_ICO[k] || 'sliders')}</span><span class="ck">${ico('check')}Aktiv</span></div><div class="pt">${p.n}</div><p>${p.d}</p><div class="meter"><span>FPS</span><div class="pips">${pips(p.fps)}</div><span>Optik</span><div class="pips">${pips(p.q)}</div></div></div>`).join('');
  $('#presets').onclick = e => { const c = e.target.closest('[data-pr]'); if (!c) return; Object.assign(S.gfxEdit, PRESETS[c.dataset.pr].v); renderGfx(true); };
  renderGfx(false);
}
function renderGfx(dirty) {
  const v = S.gfxEdit;
  const row = g => {
    const cur = v[g.k];
    if (g.r) { const n = Math.round(parseFloat(cur ?? 1) * 100); return `<div class="gc"><div class="gn"><b>${g.n}</b><span>${g.d}</span></div><div class="rng"><input type="range" min="0" max="100" value="${n}" style="--p:${n}%" data-r="${g.k}"><b>${n}%</b></div></div>`; }
    return `<div class="gc"><div class="gn"><b>${g.n}</b><span>${g.d}</span></div><div class="seg" data-g="${g.k}">${g.o.map(([n, val]) => `<button class="${String(cur) === String(val) ? 'on' : ''}" data-v="${val}">${n}</button>`).join('')}</div></div>`;
  };
  // grouped like a settings app; anything not in a group still shows up at the end
  const inG = new Set(GFX_GROUPS.flatMap(x => x[2])), rest = GFX.filter(g => !inG.has(g.k));
  $('#gctl').innerHTML = GFX_GROUPS.concat(rest.length ? [['Weitere', 'sliders', rest.map(g => g.k)]] : []).map(([n, i, ks]) =>
    `<div class="gcg"><div class="gcg-h">${ico(i)}<b>${n}</b></div><div class="gcg-b">${ks.map(k => GFX.find(g => g.k === k)).filter(Boolean).map(row).join('')}</div></div>`).join('');
  $('#gfxDirty').classList.toggle('hidden', !dirty);
  $$('.preset').forEach(p => { const pv = PRESETS[p.dataset.pr].v; p.classList.toggle('sel', Object.keys(pv).every(k => Math.abs(num(pv[k]) - num(v[k])) < .02)); });
}
document.addEventListener('click', e => { const b = e.target.closest('.gc .seg button'); if (!b) return; S.gfxEdit[b.parentElement.dataset.g] = isNaN(b.dataset.v) ? b.dataset.v : +b.dataset.v; renderGfx(true); });
document.addEventListener('input', e => { const r = e.target.closest('[data-r]'); if (!r) return; r.style.setProperty('--p', r.value + '%'); r.nextElementSibling.textContent = r.value + '%'; S.gfxEdit[r.dataset.r] = r.value / 100; $('#gfxDirty').classList.remove('hidden'); });
document.addEventListener('change', e => { if (e.target.closest('[data-r]')) renderGfx(true); });
async function gfxSave() { const r = await api('gfx.save', { values: S.gfxEdit }); if (r.error) return toast('Nicht gespeichert', r.error, false); S.gfx.values = { ...S.gfxEdit }; renderGfx(false); toast('Grafik gespeichert', 'Backup erstellt · gilt beim nächsten Start'); }
async function gfxRestore() { const r = await api('gfx.restore'); if (r.error) return toast('Kein Backup', r.error, false); await loadGfx(); toast('Backup wiederhergestellt', 'Original-Grafikeinstellungen aktiv'); }

/* ======================= INPUT LAB ======================= */
// The tester shows what the game would see: with both keys of a pair held the game cancels them out and the figure
// stands still until one is let go; with Snap Tap the later key wins. "Stillstand" is exactly that time.
const W = { up: {}, down: {}, held: new Set(), reps: [], last: 0, ov: 0 };
const OPP = { KeyA: 'KeyD', KeyD: 'KeyA', KeyW: 'KeyS', KeyS: 'KeyW' };
const snapOn = () => !!$('#snapTg')?.classList.contains('on');
/** A tester value: the number large, the unit small behind it. */
const wsVal = (id, n, unit) => { $(id).innerHTML = unit ? `${n}<small>${unit}</small>` : n; };
function resetWasd() {
  Object.assign(W, { up: {}, down: {}, held: new Set(), reps: [], last: 0, ov: 0 });
  ['#wsSwitch', '#wsStill', '#wsRepeat'].forEach(i => $(i).textContent = '–');
  $$('.key').forEach(k => k.classList.remove('dn', 'muted')); wsGame();
}
function wsGame() {
  const g = $('#wsGame'); if (!g) return;
  const snap = snapOn(); $('#wsMode').textContent = snap ? 'Snap Tap an' : 'Snap Tap aus'; $('#wsMode').classList.toggle('g', snap);
  $$('.key').forEach(k => k.classList.remove('muted'));
  let txt = '';
  for (const [a, b] of [['KeyA', 'KeyD'], ['KeyW', 'KeyS']]) {
    if (!W.held.has(a) || !W.held.has(b)) continue;
    const later = (W.down[a] || 0) > (W.down[b] || 0) ? a : b, older = later === a ? b : a, L = later.slice(3), O = older.slice(3);
    if (snap) { $(`.key[data-k="${older}"]`)?.classList.add('muted'); txt = `Snap Tap: ${L} gewinnt, ${O} wird für das Spiel losgelassen. Die Figur läuft ohne Pause in die neue Richtung.`; }
    else txt = `${O} und ${L} gleichzeitig: Das Spiel hebt beide auf, die Figur steht still, bis du ${O} loslässt.`;
  }
  g.textContent = txt || 'Halte A, drück D dazu und lass A los: so testest du einen Richtungswechsel.';
  g.classList.toggle('hot', !!txt);
}
addEventListener('keydown', e => {
  if (curPage !== 'input' || e.target.tagName === 'INPUT') return;
  const k = e.code, el = $(`.key[data-k="${k}"]`);
  if (el) e.preventDefault();
  if (e.repeat) {
    if (W.last) { W.reps.push(e.timeStamp - W.last); if (W.reps.length > 20) W.reps.shift(); wsVal('#wsRepeat', Math.round(1000 / (W.reps.reduce((a, b) => a + b, 0) / W.reps.length)), '/s'); }
    W.last = e.timeStamp; return;
  }
  W.last = e.timeStamp; W.held.add(k); W.down[k] = e.timeStamp;
  if (el) el.classList.add('dn');
  const o = OPP[k];
  if (o && W.held.has(o)) { W.ov = e.timeStamp; wsVal('#wsSwitch', 'überlappt'); }
  else if (o && W.up[o] && e.timeStamp - W.up[o] < 300) { wsVal('#wsSwitch', Math.round(e.timeStamp - W.up[o]), 'ms'); wsVal('#wsStill', 0, 'ms'); }
  wsGame();
});
addEventListener('keyup', e => {
  const k = e.code, o = OPP[k];
  if (o && W.held.has(o) && W.ov) { wsVal('#wsStill', Math.round(e.timeStamp - W.ov), 'ms'); W.ov = 0; }
  W.held.delete(k); W.up[k] = e.timeStamp; W.reps = []; W.last = 0; $(`.key[data-k="${k}"]`)?.classList.remove('dn'); wsGame();
});
document.addEventListener('click', e => { if (e.target.closest('#chopMore')) $('#chopCard').classList.toggle('open'); });
function initLab() {
  const P = { n: 0, max: 0, last: 0 }, pad = $('#pollPad');
  pad.addEventListener('onpointerrawupdate' in window ? 'pointerrawupdate' : 'pointermove', () => { P.n++; P.last = performance.now(); });
  setInterval(() => {
    const hz = P.n * 4; P.n = 0; if (performance.now() - P.last > 400) return;
    P.max = Math.max(P.max, hz);
    $('#pollHz').innerHTML = `${hz}<small>Hz</small>`;
    $('#pollTxt').textContent = `Spitze ${P.max} Hz · ` + (P.max >= 900 ? '1000-Hz-Maus' : P.max >= 450 ? '500-Hz-Maus' : 'Polling-Rate im Maus-Tool erhöhen');
    $('#pollMark')?.style.setProperty('--x', Math.min(1, P.max / 1000).toFixed(3)); pad.classList.add('meas');
  }, 250);
  $('#labTabs').onclick = e => { const b = e.target.closest('button'); if (!b) return; $$('#labTabs button').forEach(x => x.classList.toggle('on', x === b)); pad.classList.toggle('hidden', b.dataset.t !== 'poll'); $('#reactPad').classList.toggle('hidden', b.dataset.t !== 'react'); $('#padPad').classList.toggle('hidden', b.dataset.t !== 'pad'); if (b.dataset.t === 'pad') gpStart(); };
  const R = { st: 'idle', t: 0, to: 0, res: [] }, rp = $('#reactPad');
  rp.addEventListener('pointerdown', () => {
    if (R.st === 'idle' || R.st === 'done') {
      R.st = 'wait'; rp.className = 'react wait'; $('#reactMs').textContent = 'Warten…'; $('#reactTxt').textContent = 'Erst klicken, wenn es grün wird';
      R.to = setTimeout(() => { R.st = 'go'; R.t = performance.now(); rp.className = 'react go'; $('#reactMs').textContent = 'Jetzt!'; }, 1200 + Math.random() * 2600);
    } else if (R.st === 'wait') { clearTimeout(R.to); R.st = 'done'; rp.className = 'react'; $('#reactMs').textContent = 'Zu früh!'; $('#reactTxt').textContent = 'Nochmal klicken'; }
    else if (R.st === 'go') {
      const ms = performance.now() - R.t; R.res.push(ms); if (R.res.length > 5) R.res.shift(); R.st = 'done'; rp.className = 'react';
      $('#reactMs').innerHTML = `${ms.toFixed(0)}<small>ms</small>`;
      $('#reactTxt').textContent = `Ø ${(R.res.reduce((a, b) => a + b, 0) / R.res.length).toFixed(0)} ms aus ${R.res.length} Versuchen · Klick für den nächsten`;
    }
  });
}

/* ======================= CLEANER ======================= */
async function scan() { $('#cTotal').textContent = '…'; $('#cTotal').classList.remove('long'); $('#cTotalSub').textContent = 'wird gescannt…'; S.clean = await api('clean.scan'); renderClean(); }
/** "6,2 GB" as a large number with a small unit. */
const sizeHtml = b => { const t = bytes(b), i = t.lastIndexOf(' '); return i < 0 ? esc(t) : `${esc(t.slice(0, i))}<small>${esc(t.slice(i + 1))}</small>`; };
function renderClean() {
  // one surface: every area is a row (a click anywhere on it selects), its share of the largest area as a fine bar
  const max = Math.max(1, ...S.clean.map(c => Math.max(0, c.size)));
  $('#cleanList').innerHTML = S.clean.map(c => `<div class="crow ${c.sel ? 'sel' : ''}" data-cid="${c.id}" role="checkbox" aria-checked="${!!c.sel}" tabindex="0"><div class="cb ${c.sel ? 'on' : ''}"><svg viewBox="0 0 24 24"><path d="M20 6 9 17l-5-5"/></svg></div><div class="ti">${ico(c.icon || 'trash')}</div><div class="tx"><b>${esc(c.name)}</b><p>${esc(c.desc)}</p></div><div class="sz ${c.size > 0 ? '' : 'muted'}"><b>${sizeHtml(c.size)}</b><span class="lx-share"><i style="transform:scaleX(${(Math.max(0, c.size) / max).toFixed(3)})"></i></span></div></div>`).join('') || `<div class="lx-empty ok">${ico('check')}<b>Nichts zu bereinigen</b><span>Keine Caches oder Reste gefunden</span></div>`;
  const sel = S.clean.filter(c => c.sel), tot = sel.reduce((a, c) => a + Math.max(0, c.size), 0), all = S.clean.reduce((a, c) => a + Math.max(0, c.size), 0);
  const num = bytes(tot); $('#cTotal').innerHTML = sizeHtml(tot); $('#cTotal').classList.toggle('long', num.length > 6);
  $('#cTotalSub').textContent = 'ausgewählt';
  // the ring: how much of what was found is chosen
  $('#donutV').style.strokeDashoffset = 678.6 * (1 - (all > 0 ? Math.min(1, tot / all) : 0));
  $('#cSelN').textContent = `${sel.length}/${S.clean.length}`; $('#cFound').textContent = bytes(all); $('#cFreed').textContent = S.freed ? bytes(S.freed) : '0 MB';
  $('#cSelChip').textContent = sel.length ? (tot > 1073741824 ? 'Lohnt sich' : 'Bereit') : 'Nichts gewählt';
  $('#cSelTxt').textContent = sel.length ? `${sel.length} von ${S.clean.length} ausgewählt` : 'Wähle aus, was weg darf';
  $('#cAll').textContent = sel.length === S.clean.length ? 'Keine auswählen' : 'Alle auswählen';
  $('#cleanBtn').disabled = !sel.length;
}
function cleanToggle(c) { const t = S.clean.find(x => x.id === c.dataset.cid); if (!t) return; t.sel = !t.sel; renderClean(); $(`#cleanList [data-cid="${t.id}"]`)?.focus({ preventScroll: true }); }
document.addEventListener('click', e => {
  if (e.target.closest('#cAll')) { const on = !S.clean.every(c => c.sel); S.clean.forEach(c => c.sel = on); renderClean(); return; }
  const c = e.target.closest('#cleanList [data-cid]'); if (c) cleanToggle(c);
});
document.addEventListener('keydown', e => { const c = e.target.closest?.('#cleanList [data-cid]'); if (c && (e.key === ' ' || e.key === 'Enter')) { e.preventDefault(); cleanToggle(c); } });
async function clean() {
  const ids = S.clean.filter(c => c.sel).map(c => c.id); if (!ids.length) return;
  $('#cleanBtn').disabled = true; $('#cTotalSub').textContent = 'wird bereinigt…';
  const r = await api('clean.run', { ids });
  addFreed(r.freed); toast('Bereinigung fertig', bytes(r.freed) + ' freigegeben' + (r.skipped ? ' · FiveM-Cache übersprungen (Spiel läuft)' : ''));
  $('#cleanBtn').disabled = false; await scan(); $('#cTotalSub').textContent = bytes(r.freed) + ' freigegeben';
}

/* ======================= DIAGNOSE ======================= */
async function runHealth() {
  $('#hList').innerHTML = `<div class="lx-empty lx-wait">${ico('heart')}<b>Prüfe dein System</b><span>Treiber, Energieplan, Cache und mehr</span></div>`;
  $('#hScoreTxt').textContent = 'Wird geprüft…';
  const h = S.health = await api('health'); renderHealth(h); return h;
}
function renderHealth(h) {
  $('#hScore').textContent = h.score; $('#hsHealth').textContent = h.score + '%';
  if ($('#hRing')) $('#hRing').style.strokeDashoffset = 678.6 * (1 - Math.max(0, Math.min(100, h.score)) / 100);
  const cnt = st => h.items.filter(i => i.st === st).length, warn = cnt('warn'), worse = cnt('bad'), bad = h.items.length - cnt('ok');
  $('#hScoreTxt').textContent = bad ? `${bad} ${bad === 1 ? 'Punkt' : 'Punkte'} verbesserbar` : 'Alles im grünen Bereich';
  if ($('#hScoreSub')) $('#hScoreSub').textContent = bad ? 'Mit „Beheben“ stellst du sie mit einem Klick richtig.' : 'FiveM hat hier alles, was es braucht.';
  if ($('#hChip')) { $('#hChip').textContent = bad ? (worse ? `${worse} ${worse === 1 ? 'Problem' : 'Probleme'}` : 'Hinweise') : 'Optimal'; $('#hChip').className = 'chip ' + (bad ? (worse ? 'r' : 'w') : 'g'); }
  if ($('#hOk')) { $('#hOk').textContent = cnt('ok'); $('#hWarn').textContent = warn; $('#hBad').textContent = worse; }
  // problems first, then hints, then what is fine
  const order = { bad: 0, warn: 1, ok: 2 };
  $('#hList').innerHTML = h.items.map((i, n) => [i, n]).sort((a, b) => (order[a[0].st] ?? 1) - (order[b[0].st] ?? 1)).map(([i, n]) => `<div class="hc ${i.st}"><div class="st ${i.st}">${ico(i.st === 'ok' ? 'check' : 'alert')}</div><div class="tx"><b>${esc(i.name)}</b><p>${esc(i.value)}</p></div>${i.fix && i.st !== 'ok' ? `<button class="btn sm o" data-fix="${n}">Beheben</button>` : i.st === 'ok' ? '<span class="hc-ok">In Ordnung</span>' : ''}</div>`).join('');
  const hb = $('#hBtn'); if (hb) { hb.classList.remove('p'); hb.querySelector('span').textContent = 'Erneut prüfen'; }
}
document.addEventListener('click', async e => {
  const b = e.target.closest('[data-fix]'); if (!b) return;
  const i = S.health.items[+b.dataset.fix]; b.disabled = true;
  const r = await api('health.fix', { fix: i.fix }); toast(r.ok ? 'Behoben' : 'Nicht behoben', i.name, r.ok); await loadTweaks(); runHealth();
});
let logFilter = 'all', logData = { lines: [] };
const isErr = x => /error|fail|exception|crash/i.test(x);
async function loadLogs() {
  try { logData = await api('logs'); } catch { }
  renderLogs();
  $('#crashList').innerHTML = (logData.crashes || []).length
    ? logData.crashes.slice(0, 5).map(c => `<div class="svc"><span class="nm" title="${esc(c.name)}">${esc(c.name)}</span><span class="cr-m">${esc(c.date)} · ${bytes(c.size)}</span></div>`).join('')
    : `<div class="lx-empty ok">${ico('check')}<b>Keine Abstürze</b><span>FiveM ist zuletzt sauber gelaufen</span></div>`;
}
function renderLogs() {
  const l = (logData.lines || []).filter(x => logFilter === 'all' || isErr(x));
  $('#logView').innerHTML = l.length ? l.map(x => `<div class="${isErr(x) ? 'e' : /warn/i.test(x) ? 'w' : ''}">${esc(x)}</div>`).join('') : '<span class="muted">Keine Einträge</span>';
  $('#logView').scrollTop = 1e9;
}
$('#logSeg').onclick = e => { const b = e.target.closest('button'); if (!b) return; logFilter = b.dataset.f; $$('#logSeg button').forEach(x => x.classList.toggle('on', x === b)); renderLogs(); };

/* ======================= UPDATES ======================= */
async function checkUpdate(manual) {
  $('#updCheck').disabled = true;
  try {
    const u = await api('update.check');
    $('#verCur').textContent = 'v' + u.current;
    if (!u.configured) $('#updTxt').textContent = 'Update-Server nicht eingerichtet';
    else if (u.error) $('#updTxt').textContent = 'Prüfung fehlgeschlagen: ' + u.error;
    else if (u.noRelease) { $('#updTxt').textContent = 'Auf GitHub ist noch keine Version veröffentlicht'; if (manual) toast('Kein Release gefunden', 'Noch keine Version auf GitHub veröffentlicht', false); }
    else if (!u.available) { S.updAvail = false; $('#updTxt').textContent = 'Du hast die neueste Version'; if (manual) toast('Alles aktuell', 'Version ' + u.current); }
    else {
      S.updAvail = true; $('#updTxt').textContent = 'Neue Version verfügbar';
      $('#updVer').textContent = u.latest; $('#updDate').textContent = u.date || '';
      $('#updNotes').innerHTML = (u.notes || []).map(n => `<li>${esc(n)}</li>`).join('');
      // kept for the "what is new" window that opens once after this update was installed
      try { if ((u.notes || []).length) localStorage.setItem('orbitNotes', JSON.stringify({ v: u.latest, notes: u.notes })); } catch { }
      $('#updBox').classList.remove('hidden');
      $('#updPill').classList.add('show'); $('#updPillTxt').textContent = 'Update ' + u.latest;
      $('#navUpd').classList.remove('hidden');
      if (!manual && S.prompted !== u.latest) {
        S.prompted = u.latest;
        if (S.settings.autoInstall) { toast('Update wird installiert', 'Version ' + u.latest); go('settings'); doUpdate(); }
        else updatePrompt(u);
      }
    }
  } catch { $('#updTxt').textContent = 'Prüfung fehlgeschlagen'; }
  $('#updCheck').disabled = false;
}
function updatePrompt(u) {
  modal(`<div class="m-ic">${ico('download')}</div><div class="kicker">Update verfügbar</div><h1>Version <span class="grad">${esc(u.latest)}</span> ist da</h1>
    <p>Du nutzt Version ${esc(u.current)}. Soll OrbitTweak jetzt aktualisieren? Die App startet danach automatisch neu.</p>
    ${(u.notes || []).length ? `<ul class="changes m-list">${u.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul>` : ''}
    <div class="bar m-bar hidden" id="mUpdMeter"><i id="mUpdBar" style="width:0"></i></div><div class="m-prog" id="mUpdProg"></div>
    <div class="act" id="mUpdAct"><button class="btn lg" onclick="closeModal()">Später</button><button class="btn p lg" onclick="promptInstall()">${ico('download')}Jetzt aktualisieren</button></div>`);
}
function promptInstall() { $('#mUpdAct').classList.add('hidden'); $('#mUpdMeter').classList.remove('hidden'); $('#mUpdProg').textContent = 'Lade herunter…'; doUpdate(); }
function updProgress(d) {
  const mb = $('#mUpdBar'); if (mb) { mb.style.width = d.pct + '%'; $('#mUpdProg').textContent = d.total ? `${bytes(d.done)} / ${bytes(d.total)}` : ''; }
  $('#updMeter').classList.remove('hidden'); $('#updBar').style.width = d.pct + '%';
  $('#updProg').textContent = d.total ? `${bytes(d.done)} / ${bytes(d.total)}` : '';
}
async function doUpdate() {
  const b = $('#updBtn'); b.disabled = true; b.querySelector('span').textContent = 'Lädt…';
  try {
    const r = await api('update.download');
    if (r.error) throw new Error(r.error);
    $('#updProg').textContent = 'Installiere, OrbitTweak startet gleich neu…';
    if ($('#mUpdProg')) $('#mUpdProg').textContent = 'Installiere, OrbitTweak startet gleich neu…';
    await sleep(400); await api('update.install');
  } catch (e) {
    toast('Update fehlgeschlagen', e.message, false); b.disabled = false; b.querySelector('span').textContent = 'Erneut versuchen';
    if ($('#mUpdAct')) { $('#mUpdAct').classList.remove('hidden'); $('#mUpdProg').textContent = e.message; }
  }
}

/* ======================= SETTINGS ======================= */
const SETS = [
  ['autoUpdate', 'Automatisch nach Updates suchen', 'Beim Start und danach alle 2 Stunden'],
  ['autoInstall', 'Updates ohne Nachfrage installieren', 'Aus: OrbitTweak fragt vorher, ob es aktualisieren darf'],
  ['restoreBeforeBoost', 'Wiederherstellungspunkt vor dem Boost', 'Windows-Snapshot vor jeder Optimierung'],
  ['purgeStandby', 'Standby-Liste beim RAM-Clean leeren', 'Gibt gecachten Speicher frei (empfohlen bei 8–16 GB)'],
  ['autoRamClean', 'Auto-RAM-Clean im Spiel', 'Alle 5 Minuten, solange FiveM läuft'],
  ['autoPriority', 'Auto-Priorität „Hoch“', 'GTA-Prozess bekommt automatisch mehr CPU-Zeit'],
];
function renderSettings() {
  const cur = themeKey(S.settings.accent);
  $('#themes').innerHTML = Object.entries(THEMES).filter(([k]) => !SCENE_THEMES.includes(k)).map(([k, [n, a, b]]) => `<div class="sw ${k === cur ? 'on' : ''}" data-th="${k}" title="${n}"><i style="--sa:${a};--sb:${b}"></i>${n}</div>`).join('');
  const SCN = { winter: 'Nordlicht, Raureif, Schnee', anime: 'Abendhimmel, Kirschblüten' };
  if ($('#sceneThemes')) $('#sceneThemes').innerHTML = SCENE_THEMES.map(k => `<button class="scn-c scn-${k} ${k === cur ? 'on' : ''}" data-th="${k}"><i></i><span><b>${THEMES[k][0]}</b><small>${SCN[k] || ''}</small></span>${k === cur ? ico('check', 'i scn-ck') : ''}</button>`).join('');
  $('#fxTg')?.classList.toggle('on', S.settings.fx !== false);
  $('#fxRow')?.classList.toggle('hidden', !FXCFG[cur]);
  const row = ([k, n, d]) => `<div class="srow sclick"><div><b>${n}</b><p>${d}</p></div><div class="tg ${S.settings[k] ? 'on' : ''}" data-set="${k}"></div></div>`;
  // RAM-Clean and priority are the live tweaks of Tweaks › FiveM: one switch, in one place
  $('#setList').innerHTML = SETS.filter(s => !['autoUpdate', 'autoInstall', 'autoRamClean', 'autoPriority'].includes(s[0])).map(row).join('')
    + `<button class="srow slink" onclick="goTweaks('fivem')"><div><b>Live-Tweaks im Spiel</b><p>RAM-Clean und Priorität „Hoch“ stellst du unter Tweaks › FiveM ein</p></div>${ico('chev', 'i ch')}</button>`;
  $('#updSets').innerHTML = SETS.filter(s => ['autoUpdate', 'autoInstall'].includes(s[0])).map(row).join('');
}
document.addEventListener('click', e => {
  if (e.target.closest('#fxRow')) {   // snow / petals on or off
    S.settings.fx = S.settings.fx === false; renderSettings(); fxStart(themeKey(S.settings.accent)); api('settings.set', { key: 'fx', value: S.settings.fx }); return;
  }
  const th = e.target.closest('[data-th]');
  if (th) { S.settings.accent = th.dataset.th; applyTheme(th.dataset.th); renderSettings(); api('settings.set', { key: 'accent', value: th.dataset.th }); padDraw(); padPush(); return; }
  const st = e.target.closest('[data-set]') || e.target.closest('.sclick')?.querySelector('[data-set]');
  if (st) { const k = st.dataset.set; S.settings[k] = !S.settings[k]; st.classList.toggle('on'); api('settings.set', { key: k, value: S.settings[k] }); if (!k.startsWith('auto') || k === 'autoRamClean' || k === 'autoPriority') loadTweaks(); }
});
function goTweaks(cat) { curTab = cat; go('tweaks'); renderTweaks(); }
window.goTweaks = goTweaks;
/* ======================= DISCORD STATUS (settings › Verhalten) ======================= */
const DC_SETS = [
  ['discordOn', 'Status in Discord zeigen', 'Dein Profil zeigt „Spielt OrbitTweak“, ob du im Spiel bist und wie lange'],
  ['discordServer', 'Servername zeigen', 'Aus: nur „FiveM“, ohne Servername und Server-Icon'],
  ['discordKills', 'Kills zeigen', 'Kills der Runde, z. B. „Im Spiel · 3 Kills“. Braucht die Kill-Erkennung'],
];
let DC = null;
const dcOn = k => S.settings[k] !== false;   // all three are on by default
async function dcLoad() { try { DC = await api('discord.state'); } catch { return; } dcRender(); }
function dcRender() {
  if (!$('#dcCard')) return;
  const noId = DC ? DC.status === 'noid' || (!DC.builtIn && !S.settings.discordAppId) : !S.settings.discordAppId;
  // only when something changed: swapping the rows every 3 s would swallow a click that lands in between
  // without an App-ID the setup comes first and the switches wait for it
  $('#dcCard').classList.toggle('noid', !!noId);
  // no OrbitTweak Discord app is set up: the card stays hidden instead of asking users to create one themselves
  $('#dcCard').classList.toggle('hidden', !!DC && !DC.builtIn && !S.settings.discordAppId);
  const rows = (noId ? '<p class="dc-need">Erst App-ID eintragen, dann lässt sich der Status einschalten.</p>' : '')
    + DC_SETS.map(([k, n, d]) => `<div class="srow sclick ${noId || (k !== 'discordOn' && !dcOn('discordOn')) ? 'dis' : ''}" data-dcset="${k}"><div><b>${n}</b><p>${d}</p></div><div class="tg ${dcOn(k) ? 'on' : ''}"></div></div>`).join('');
  if ($('#dcSets').dataset.html !== rows) { $('#dcSets').innerHTML = rows; $('#dcSets').dataset.html = rows; }
  $('#dcGuide').classList.toggle('hidden', !noId);
  const inp = $('#dcAppId');
  if (document.activeElement !== inp && !inp.dataset.dirty) inp.value = S.settings.discordAppId || '';   // keeps a typed, unsaved ID
  if (DC?.builtIn) inp.placeholder = 'Leer: App von OrbitTweak';
  if (!DC) return;
  const v = DC.preview, min = v?.since ? Math.max(0, Math.round((Date.now() - new Date(v.since)) / 60000)) : null;
  const time = min == null ? '' : min < 60 ? `seit ${min} min` : `seit ${Math.floor(min / 60)} h ${String(min % 60).padStart(2, '0')}`;
  const shown = v ? [v.details, v.state, time].filter(Boolean).join(' · ') : '';
  const [cls, txt] = {
    on: ['g', 'Verbunden' + (DC.user ? ' als ' + DC.user : '')],
    nodiscord: ['w', 'Discord nicht offen. Verbindet sich, sobald Discord läuft'],
    refused: ['r', 'Discord kennt diese App-ID nicht. Prüf die ID'],
    noid: ['w', 'Keine App-ID. Folge der Anleitung oben'],
    off: ['', 'Aus. Discord zeigt nichts von OrbitTweak'],
  }[DC.status] || ['', ''];
  $('#dcLive').className = 'dc-live ' + cls;
  $('#dcLiveTx').innerHTML = esc(txt) + (DC.status === 'on' && shown ? `<small>Zeigt gerade: ${esc(shown)}</small>` : '')
    + (DC.error && DC.status !== 'off' && DC.status !== 'noid' ? `<small class="dc-err">Discord: ${esc(DC.error)}</small>` : '');
}
async function dcSaveId() {
  const raw = $('#dcAppId').value.trim(), m = raw.match(/(?<!\d)\d{17,20}(?!\d)/);
  if (raw && !m) return toast('Keine gültige App-ID', 'Die Application ID hat 17 bis 20 Ziffern', false);
  const id = m ? m[0] : '';
  try { await api('settings.set', { key: 'discordAppId', value: id }); } catch (e) { return toast('Nicht gespeichert', e.message, false); }
  if (id && !S.settings.discordAppId) S.settings.discordOn = true;   // on by default once there is an ID (the host does the same)
  S.settings.discordAppId = id; $('#dcAppId').value = id; delete $('#dcAppId').dataset.dirty;
  toast(id ? 'App-ID gespeichert' : 'App-ID entfernt', id ? 'OrbitTweak verbindet sich mit Discord' : '');
  dcLoad();
}
document.addEventListener('click', async e => {
  if (e.target.closest('#dcSave')) return dcSaveId();
  const r = e.target.closest('[data-dcset]'); if (!r) return;
  const k = r.dataset.dcset; S.settings[k] = !dcOn(k); dcRender();
  try { await api('settings.set', { key: k, value: S.settings[k] }); } catch { }
  dcLoad();
});
document.addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.id === 'dcAppId') dcSaveId(); });
document.addEventListener('input', e => { if (e.target.id === 'dcAppId') e.target.dataset.dirty = 1; });
// the status line stays current while the card is open (Discord started or closed, game state, kills)
setInterval(() => { if (curPage === 'settings' && !$('#st-general')?.classList.contains('hidden')) dcLoad(); }, 3000);

async function restorePoint() { toast('Wird erstellt…', 'Kann bis zu einer Minute dauern'); const r = await api('restore'); toast(r.ok ? 'Wiederherstellungspunkt erstellt' : 'Fehlgeschlagen', r.ok ? 'Du kannst jederzeit zurück' : 'Ist der Computerschutz aktiviert?', r.ok); }

/* ======================= MODAL / WIZARD ======================= */
function modal(html) { $('#mbox').innerHTML = html; $('#modal').classList.add('show'); }
function closeModal() {
  $('#modal').classList.remove('show');
  // a clip that is still playing must stop with its window (picture and sound), whichever way the window was closed
  $$('#mbox video, #mbox audio').forEach(v => { try { v.pause(); v.removeAttribute('src'); v.load(); } catch { } });
  setTimeout(() => { if (!$('#modal').classList.contains('show')) { $('#mbox').innerHTML = ''; $('#mbox').style.width = ''; } }, 400);
}
function confirmBox(title, text, o = {}) {
  return new Promise(res => {
    modal(`<div class="m-cf ${o.danger ? 'danger' : ''}"><div class="m-ic">${ico(o.danger ? 'alert' : 'shield')}</div><div><h1>${esc(title)}</h1><p>${esc(text)}</p></div></div><div class="act"><button class="btn lg" id="mNo">Abbrechen</button><button class="btn lg ${o.danger ? 'dp' : 'p'}" id="mYes">${esc(o.ok || 'Bestätigen')}</button></div>`);
    $('#mNo').onclick = () => { closeModal(); res(false); }; $('#mYes').onclick = () => { closeModal(); res(true); };
  });
}
const steps = n => `<div class="steps"><span>Schritt ${n + 1} von 4</span>${[0, 1, 2, 3].map(i => `<i class="${i <= n ? 'on' : ''}"></i>`).join('')}</div>`;
const LOGO_MARK = '<svg viewBox="0 0 64 64"><circle cx="32" cy="32" r="17" fill="none" stroke="url(#rg)" stroke-width="6"/><ellipse cx="32" cy="32" rx="29" ry="10" fill="none" stroke="url(#rg)" stroke-width="3.2" transform="rotate(-28 32 32)"/><circle cx="52" cy="13" r="4" fill="#fff"/></svg>';
async function wizard(step) {
  if (step === 0) modal(`${steps(0)}<div class="m-mark">${LOGO_MARK}</div><div class="kicker">Setup-Assistent</div><h1>Willkommen bei <span class="grad">OrbitTweak</span></h1><p>In einer Minute ist dein PC bereit für FiveM: Wir prüfen dein System, zeigen dir die Optimierungen und wenden sie mit Wiederherstellungspunkt an. Alles bleibt umkehrbar.</p><div class="act"><button class="btn lg" onclick="finishWizard()">Überspringen</button><button class="btn p lg" onclick="wizard(1)">${ico('zap')}Los geht's</button></div>`);
  if (step === 1) {
    modal(`${steps(1)}<h1>System-<span class="grad">Scan</span></h1><p>Analysiere Hardware, Windows und FiveM…</p><div class="bar m-bar"><i style="width:30%" id="scanBar"></i></div>`);
    const [h] = await Promise.all([runHealth(), sleep(1000)]);
    $('#scanBar').style.width = '100%'; await sleep(300);
    const todo = S.tweaks.filter(t => t.rec && !t.applied);
    const cats = Object.entries(CATS).filter(([k]) => todo.some(t => t.cat === k));
    modal(`${steps(2)}<h1>Dein System-<span class="grad">Check</span></h1><p>${todo.length ? 'Das kann OrbitTweak für FiveM verbessern. Jede Änderung lässt sich einzeln zurücknehmen.' : 'Alles Empfohlene ist schon aktiv.'}</p>
      <div class="m-nums"><div><b>${h.score}<small>%</small></b><span>Zustand</span></div><div><b>${todo.length}</b><span>Empfohlen</span></div><div><b>${cats.length}</b><span>${cats.length === 1 ? 'Bereich' : 'Bereiche'}</span></div></div>
      <div class="wl">${cats.map(([k, [n, i]]) => { const c = todo.filter(t => t.cat === k); return `<div class="hc"><div class="st">${ico(i)}</div><div class="tx"><b>${n}</b><p>${c.map(t => esc(t.name)).join(' · ')}</p></div><span class="m-n">${c.length}</span></div>`; }).join('')}</div><div class="act"><button class="btn lg" onclick="finishWizard()">Später</button><button class="btn p lg" onclick="wizard(3)">${ico('zap')}Optimieren</button></div>`);
    const wl = $('#mbox .wl'); if (wl && wl.scrollHeight > wl.clientHeight + 4) wl.classList.add('more');
  }
  if (step === 3) {
    modal(`${steps(3)}<h1>Boost <span class="grad">läuft</span></h1><p id="wizTxt">Starte…</p><div class="bar m-bar"><i id="wizProg" style="width:0"></i></div>`);
    const r = await boost(true);
    modal(`${steps(3)}<div class="m-ic ok">${ico('check')}</div><h1>Dein PC ist <span class="grad">FiveM-ready</span></h1><p>${r ? 'Alles ist angewendet und bleibt umkehrbar.' : 'Der Boost ist durch.'}</p>
      ${r ? `<div class="m-nums"><div><b>${r.applied}</b><span>Optimierungen</span></div><div><b>${bytes(r.freed).replace(/\s?([A-Z]+)$/, '<small>$1</small>')}</b><span>Freigegeben</span></div></div>` : ''}
      ${r && r.restart ? `<div class="m-note">${ico('alert')}<span>Starte deinen PC neu, damit alle Tweaks greifen.</span></div>` : ''}<div class="act"><button class="btn p lg" onclick="finishWizard()">Fertig</button></div>`);
  }
}
function finishWizard() { closeModal(); api('settings.set', { key: 'firstRun', value: false }); S.settings.firstRun = false; }

/* ======================= CROSSHAIR ======================= */
const KEYS = [['Aus', 0], ['F6', 0x75], ['F7', 0x76], ['F9', 0x78], ['F10', 0x79], ['F11', 0x7A], ['Einfg', 0x2D], ['Pos1', 0x24], ['Bild↑', 0x21]];
const XH_DEF = { style: 'cross', color: '#3DFF8B', size: 8, thick: 2, gap: 4, alpha: 100, outline: true, oThick: 1, dot: false, dotSize: 2 };
const XH_PRESETS = {
  classic: ['Klassisch', { style: 'cross', color: '#3DFF8B', size: 8, thick: 2, gap: 4, alpha: 100, outline: true, oThick: 1, dot: false, dotSize: 2 }],
  dot: ['Punkt', { style: 'dot', color: '#FF3B5C', size: 8, thick: 2, gap: 4, alpha: 100, outline: true, oThick: 1, dot: true, dotSize: 4 }],
  circle: ['Kreis', { style: 'circle', color: '#3DE0FF', size: 10, thick: 2, gap: 4, alpha: 100, outline: true, oThick: 1, dot: true, dotSize: 2 }],
  sniper: ['Sniper', { style: 'cross', color: '#FFE03D', size: 16, thick: 1, gap: 7, alpha: 100, outline: true, oThick: 1, dot: true, dotSize: 2 }],
  tshape: ['T-Form', { style: 't', color: '#FFFFFF', size: 9, thick: 2, gap: 3, alpha: 100, outline: true, oThick: 1, dot: false, dotSize: 2 }],
};
const XH_STYLES = [
  ['cross', 'Kreuz', '<path d="M12 3v6M12 15v6M3 12h6M15 12h6"/>'],
  ['t', 'T-Form', '<path d="M12 15v6M3 12h6M15 12h6"/>'],
  ['dot', 'Punkt', '<circle cx="12" cy="12" r="3" fill="currentColor"/>'],
  ['circle', 'Kreis', '<circle cx="12" cy="12" r="7"/>'],
  ['plus', 'Plus', '<path d="M12 4v16M4 12h16"/>'],
];
const XH_COLORS = ['#3DFF8B', '#FFFFFF', '#3DE0FF', '#FFE03D', '#FF3B5C', '#FF4DF0', '#FF8A3D'];
const XH_SLIDERS = [['size', 'Länge', 1, 40], ['thick', 'Dicke', 1, 8], ['gap', 'Lücke', 0, 20], ['alpha', 'Deckkraft', 20, 100, '%'], ['oThick', 'Kontur', 0, 3], ['dotSize', 'Punkt', 1, 8], ['offsetX', 'Versatz X', -60, 60], ['offsetY', 'Versatz Y', -60, 60]];
const XH_GROUPS = [['Maße', ['size', 'thick', 'gap', 'dotSize']], ['Kontur und Deckkraft', ['oThick', 'alpha']], ['Position', ['offsetX', 'offsetY']]];
const XH_RULES = 'Manche FiveM-Server verbieten Fadenkreuz-Overlays. Prüfe vorher die Regeln deines Servers, die Nutzung erfolgt auf eigene Verantwortung.';
const XH = { cfg: { ...XH_DEF }, on: false, onlyGame: false, key: 0x79, offsetX: 0, offsetY: 0 };

function xhDraw(c) {
  const o = c.outline ? c.oThick : 0, t = c.thick, g = c.style === 'plus' ? 0 : c.gap, L = c.size;
  const R = Math.ceil(Math.max(c.style === 'dot' ? 0 : L + g + t, c.dot || c.style === 'dot' ? c.dotSize : 0) + o + 3);
  const N = 2 * R + 1, cv = document.createElement('canvas'); cv.width = cv.height = N;
  const x = cv.getContext('2d'), m = R, t0 = m - Math.floor((t - 1) / 2);
  const shapes = [];
  if (c.style === 'cross' || c.style === 't' || c.style === 'plus') {
    shapes.push([m + 1 + g, t0, L, t], [m - g - L, t0, L, t]);
    shapes.push([t0, m + 1 + g, t, L]);
    if (c.style !== 't') shapes.push([t0, m - g - L, t, L]);
    if (g === 0) shapes.push([t0, t0, t, t]);
  }
  const dot = c.dot || c.style === 'dot', ds = c.style === 'dot' ? c.dotSize : c.dotSize;
  const paint = (col, e) => {
    x.fillStyle = col; x.strokeStyle = col;
    shapes.forEach(([a, b, w, h]) => x.fillRect(a - e, b - e, w + 2 * e, h + 2 * e));
    if (c.style === 'circle') { x.lineWidth = t + 2 * e; x.beginPath(); x.arc(m + .5, m + .5, L, 0, Math.PI * 2); x.stroke(); }
    if (dot) {
      if (ds <= 2) { const d0 = m - Math.floor((ds - 1) / 2); x.fillRect(d0 - e, d0 - e, ds + 2 * e, ds + 2 * e); }
      else { x.beginPath(); x.arc(m + .5, m + .5, ds / 2 + e, 0, Math.PI * 2); x.fill(); }
    }
  };
  if (o > 0) paint('#000', o);
  paint(c.color, 0);
  if (c.alpha >= 100) return cv;
  const out = document.createElement('canvas'); out.width = out.height = N;
  const ox = out.getContext('2d'); ox.globalAlpha = c.alpha / 100; ox.drawImage(cv, 0, 0); return out;
}
function xhRender(bump) {
  const src = xhDraw(XH.cfg), cv = $('#xhCanvas'), z = $('#xhZoom');
  cv.width = src.width; cv.height = src.height; cv.getContext('2d').drawImage(src, 0, 0);
  xhPlace();
  const zx = z.getContext('2d'); zx.clearRect(0, 0, 96, 96); zx.imageSmoothingEnabled = false;
  const s = Math.max(24, Math.min(src.width, 48)), sx = (src.width - s) / 2; zx.drawImage(src, sx, sx, s, s, 0, 0, 96, 96);
  if (bump) { const sc = $('#xhScene'); sc.classList.remove('bump'); void sc.offsetWidth; sc.classList.add('bump'); }
  return src;
}
// same math as the overlay window: middle pixel of the odd-sized image sits on the center pixel
function xhPlace() {
  const sc = $('#xhScene'), cv = $('#xhCanvas'); if (!sc || !cv.width) return;
  const R = (cv.width - 1) / 2;
  cv.style.left = (Math.floor(sc.clientWidth / 2) - R + XH.offsetX) + 'px';
  cv.style.top = (Math.floor(sc.clientHeight / 2) - R + XH.offsetY) + 'px';
}
addEventListener('resize', () => xhPlace());
async function copyCmd(text, btn) {
  let ok = false;
  try { const r = await api('clip', { text }); ok = r && r.ok; } catch { }
  if (!ok) try { await navigator.clipboard.writeText(text); ok = true; } catch { }
  if (btn) { btn.classList.add('ok'); const o = btn.innerHTML; btn.innerHTML = `${ico('check')}Kopiert`; setTimeout(() => { btn.classList.remove('ok'); btn.innerHTML = o; }, 1600); }
  toast(ok ? 'Befehl kopiert' : 'Kopieren fehlgeschlagen', ok ? `${text} · In FiveM: F8, Strg+V, Enter` : text, ok);
}
window.copyCmd = copyCmd;
let xhT = 0;
function xhPush(img = true) {
  clearTimeout(xhT);
  xhT = setTimeout(async () => {
    const src = img ? xhDraw(XH.cfg) : null;
    const r = await api('xhair.set', { png: src ? src.toDataURL('image/png').split(',')[1] : '', json: JSON.stringify(XH.cfg), on: XH.on, onlyGame: XH.onlyGame, key: XH.key, offsetX: XH.offsetX, offsetY: XH.offsetY }).catch(() => null);
    if (r && r.keyOk === false) toast('Taste belegt', 'Eine andere App nutzt diese Taste schon', false);
  }, 90);
}
function xhUI() {
  $('#xhPresets').innerHTML = Object.entries(XH_PRESETS).map(([k, [n, c]]) => { const on = JSON.stringify(c) === JSON.stringify(XH.cfg); return `<button class="xpre ${on ? 'on' : ''}" data-xp="${k}"><canvas data-pv="${k}"></canvas>${n}</button>`; }).join('');
  $$('[data-pv]').forEach(cv => { const s = xhDraw(XH_PRESETS[cv.dataset.pv][1]); cv.width = s.width; cv.height = s.height; cv.getContext('2d').drawImage(s, 0, 0); });
  $('#xhStyles').innerHTML = XH_STYLES.map(([k, n, svg]) => `<button class="xst ${XH.cfg.style === k ? 'on' : ''}" data-xs="${k}"><svg viewBox="0 0 24 24">${svg}</svg>${n}</button>`).join('');
  const custom = !XH_COLORS.includes(XH.cfg.color.toUpperCase());
  $('#xhColors').innerHTML = XH_COLORS.map(c => `<button class="xcol ${XH.cfg.color.toUpperCase() === c ? 'on' : ''}" data-xc="${c}" style="background:${c}"></button>`).join('') +
    `<label class="xcustom ${custom ? 'on' : ''}" title="Eigene Farbe"><input type="color" id="xhPick" value="${XH.cfg.color}"></label>`;
  const val = k => k === 'offsetX' || k === 'offsetY' ? XH[k] : XH.cfg[k];
  const slider = ([k, n, mi, ma, u = '']) => { const v = val(k), p = (v - mi) / (ma - mi) * 100; return `<div class="xs"><span>${n}</span><input type="range" min="${mi}" max="${ma}" value="${v}" style="--p:${p}%" data-xr="${k}"><b>${v}${u}</b></div>`; };
  const group = (h, keys) => `<div class="xsec"><div class="xsec-h">${h}</div>${keys.map(k => XH_SLIDERS.find(x => x[0] === k)).filter(Boolean).map(slider).join('')}</div>`;
  $('#xhSliders').innerHTML = `<div class="xsec xtgl"><div class="xs tgl"><span>Schwarze Kontur</span><div class="tg ${XH.cfg.outline ? 'on' : ''}" data-xb="outline"></div></div><div class="xs tgl"><span>Mittelpunkt</span><div class="tg ${XH.cfg.dot ? 'on' : ''}" data-xb="dot"></div></div></div>` +
    XH_GROUPS.map(([h, keys]) => group(h, keys)).join('');
  xhState();
  xhRender();
}
function xhState() {
  $('#xhShrink').classList.toggle('on', S.settings.xhairShrink !== false);
  $('#xhOn')?.classList.toggle('on', XH.on); $('#xhOnly').classList.toggle('on', XH.onlyGame);
  $('#xhMain').querySelector('span').textContent = XH.on ? 'Ausschalten' : 'Einschalten';
  $('#xhMain').classList.toggle('p', !XH.on);
  $('#navXh').classList.toggle('hidden', !XH.on);
  $('#xhKey').innerHTML = KEYS.map(([n, v]) => `<button class="${XH.key === v ? 'on' : ''}" data-xk="${v}">${n}</button>`).join('');
  const kn = KEYS.find(k => k[1] === XH.key)?.[0];
  $('#xhKeyTxt').textContent = XH.key ? `${kn} schaltet das Fadenkreuz im Spiel an und aus` : 'Keine Taste, nur hier umschaltbar';
  const lv = $('#xhLive'); if (lv) { lv.className = 'chip' + (XH.on ? ' g' : ''); lv.textContent = XH.on ? (kn && XH.key ? `An · ${kn}` : 'An') : 'Aus'; }
}
function xhToggle(confirmed) {
  if (!XH.on && !S.settings.xhairAck && !confirmed) {
    return modal(`<div class="kicker">Hinweis</div><h1 style="margin-top:10px">Server-Regeln <span class="grad">beachten</span></h1>
      <p>${XH_RULES}</p><p>OrbitTweak zeigt das Fadenkreuz nur als Fenster über dem Spiel an und verändert nichts an FiveM.</p>
      <div class="act"><button class="btn lg" onclick="closeModal()">Abbrechen</button><button class="btn p lg" onclick="xhAck()">${ico('check')}Verstanden, einschalten</button></div>`);
  }
  XH.on = !XH.on; xhState(); xhPush(false);
  if (XH.on && S.settings.xhairShrink !== false) setTimeout(() => toast('GTA-Fadenkreuz wird minimiert', 'OrbitTweak sendet profile_reticuleSize 0 an FiveM'), 700);
  toast(XH.on ? 'Fadenkreuz an' : 'Fadenkreuz aus', XH.on ? 'Server-Regeln beachten · ' + (XH.onlyGame ? 'nur im Spiel sichtbar' : 'bleibt auch beim Raustabben') : '');
}
function xhAck() { closeModal(); S.settings.xhairAck = true; api('settings.set', { key: 'xhairAck', value: true }); xhToggle(true); }
async function setBorderless() {
  const r = await api('gfx.save', { values: { Windowed: 2 } });
  if (r.error) return toast('Nicht gespeichert', r.error, false);
  if (S.gfx) { S.gfx.values.Windowed = 2; S.gfxEdit.Windowed = 2; }
  toast('Randlos eingestellt', 'Gilt ab dem nächsten FiveM-Start');
}
document.addEventListener('click', e => {
  const p = e.target.closest('[data-xp]'); if (p) { XH.cfg = { ...XH_PRESETS[p.dataset.xp][1] }; xhUI(); xhRender(true); xhPush(); return; }
  const s = e.target.closest('[data-xs]'); if (s) { XH.cfg.style = s.dataset.xs; xhUI(); xhRender(true); xhPush(); return; }
  const c = e.target.closest('[data-xc]'); if (c) { XH.cfg.color = c.dataset.xc; xhUI(); xhPush(); return; }
  const b = e.target.closest('[data-xb]'); if (b) { XH.cfg[b.dataset.xb] = !XH.cfg[b.dataset.xb]; xhUI(); xhPush(); return; }
  const k = e.target.closest('[data-xk]'); if (k) { XH.key = +k.dataset.xk; xhState(); xhPush(false); return; }
  const t = e.target.closest('[data-xt]'); if (t) { if (t.dataset.xt === 'on') return xhToggle(); XH.onlyGame = !XH.onlyGame; xhState(); xhPush(false); return; }
  const bg = e.target.closest('#xhBg button'); if (bg) xhScene(bg.dataset.bg);
});
document.addEventListener('input', e => {
  const r = e.target.closest('[data-xr]');
  if (r) {
    const k = r.dataset.xr, v = +r.value, u = k === 'alpha' ? '%' : '';
    if (k === 'offsetX' || k === 'offsetY') XH[k] = v; else XH.cfg[k] = v;
    r.style.setProperty('--p', (v - r.min) / (r.max - r.min) * 100 + '%'); r.nextElementSibling.textContent = v + u;
    $$('.xpre').forEach(x => x.classList.toggle('on', JSON.stringify(XH_PRESETS[x.dataset.xp][1]) === JSON.stringify(XH.cfg)));
    xhRender(); xhPush(); return;
  }
  if (e.target.id === 'xhPick') { XH.cfg.color = e.target.value.toUpperCase(); xhRender(); xhPush(); }
});
document.addEventListener('change', e => { if (e.target.id === 'xhPick') xhUI(); });
document.addEventListener('click', e => { if (!e.target.closest('#xhShrink')) return; S.settings.xhairShrink = S.settings.xhairShrink === false; api('settings.set', { key: 'xhairShrink', value: S.settings.xhairShrink }); xhState(); });

/* ======================= SCREEN COLOR ======================= */
const DP_PRESETS = {
  std: ['Standard', 'Original', { vibrance: 0, brightness: 0, contrast: 0, gamma: 100 }],
  vivid: ['Lebendig', 'Kräftig', { vibrance: 45, brightness: 0, contrast: 8, gamma: 100 }],
  night: ['Nachtsicht', 'Schatten heller', { vibrance: 20, brightness: 10, contrast: -4, gamma: 140 }],
  sharp: ['Kontrast', 'Klare Kanten', { vibrance: 25, brightness: -3, contrast: 20, gamma: 95 }],
  cine: ['Kino', 'Warm', { vibrance: 12, brightness: -3, contrast: 12, gamma: 90 }],
};
const DP_SL = [['vibrance', 'Farbintensität', 'NVIDIA Digital Vibrance', 0, 100], ['brightness', 'Helligkeit', 'Gesamtes Bild', -50, 50], ['contrast', 'Kontrast', 'Abstand hell zu dunkel', -50, 50], ['gamma', 'Gamma', 'Hellt Schatten auf', 50, 200]];
const DP = { cfg: { on: false, onlyGame: true, vibrance: 0, brightness: 0, contrast: 0, gamma: 100, key: 0 }, nvidia: false, unlocked: false, gammaOk: null, vibOk: null };
const dpFmt = (k, v) => k === 'gamma' ? (v / 100).toFixed(2).replace('.', ',') : k === 'vibrance' ? v + '%' : (v > 0 ? '+' : '') + v;
const dpFilter = c => `saturate(${1 + c.vibrance / 45}) brightness(${1 + c.brightness / 70 + (c.gamma - 100) / 220}) contrast(${1 + c.contrast / 60})`;
function dpUI() {
  const c = DP.cfg;
  $('#dpPresets').innerHTML = Object.entries(DP_PRESETS).map(([k, [n, d, v]]) => { const on = Object.keys(v).every(x => v[x] === c[x]); return `<button class="dpre ${on ? 'on' : ''}" data-dp="${k}"><i style="filter:${dpFilter(v)}"></i><b>${n}</b><span>${d}</span></button>`; }).join('');
  $('#dpSliders').innerHTML = DP_SL.map(([k, n, d, mi, ma]) => {
    const v = c[k], p = (v - mi) / (ma - mi) * 100, off = k === 'vibrance' && !DP.nvidia;
    return `<div class="dps ${off ? 'off' : ''}"><div class="n"><b>${n}</b><span>${off ? 'Nur mit NVIDIA-Grafikkarte' : d}</span></div><input type="range" min="${mi}" max="${ma}" value="${v}" style="--p:${p}%" data-dr="${k}" ${off ? 'disabled' : ''}><b>${dpFmt(k, v)}</b></div>`;
  }).join('');
  $('#dpKey').innerHTML = KEYS.map(([n, v]) => `<button class="${c.key === v ? 'on' : ''}" data-dk="${v}">${n}</button>`).join('');
  const kn = KEYS.find(k => k[1] === c.key)?.[0];
  $('#dpKeyTxt').textContent = c.key ? `${kn} schaltet das Farbprofil im Spiel an und aus` : 'Profil im Spiel schnell umschalten';
  dpState();
  const u = bgUrl(); $$('.dpre i').forEach(i => i.style.backgroundImage = `url('${u}')`); $$('.dpimg').forEach(d => d.style.backgroundImage = `url('${u}')`);
}
function dpState() {
  const c = DP.cfg;
  $('#dpOnly').classList.toggle('on', c.onlyGame);
  $('#dpMain').querySelector('span').textContent = c.on ? 'Ausschalten' : 'Einschalten';
  $('#dpMain').classList.toggle('p', !c.on);
  $('#navDp').classList.toggle('hidden', !c.on);
  $('#dpLive').className = 'chip ' + (c.on ? 'g' : ''); $('#dpLive').textContent = c.on ? (c.onlyGame ? 'An · nur im Spiel' : 'An') : 'Aus';
  $('#dpAfter').style.filter = dpFilter(c.on ? c : DP_PRESETS.std[2]);
  const st = [];
  st.push(DP.nvidia ? `<div class="dstat ok">${ico('check')}<div><b>NVIDIA erkannt.</b> Farbintensität wird direkt im Treiber gesetzt.</div></div>`
    : `<div class="dstat warn">${ico('alert')}<div><b>Farbintensität nur mit NVIDIA.</b> Bei AMD: Radeon Software → Anzeige → Sättigung. Helligkeit, Kontrast und Gamma funktionieren überall.</div></div>`);
  if (DP.gammaOk === false && !DP.unlocked) st.push(`<div class="dstat warn">${ico('alert')}<div><b>Windows begrenzt die Stärke.</b> Schalte den vollen Gamma-Bereich frei, danach einmal neu starten.<br><button class="btn sm" onclick="dpUnlock()">${ico('zap')}Freischalten</button></div></div>`);
  else if (DP.gammaOk === false) st.push(`<div class="dstat warn">${ico('alert')}<div><b>Wert zu stark.</b> Starte den PC neu, falls du den Bereich gerade freigeschaltet hast, oder nimm etwas zurück.</div></div>`);
  st.push(`<div class="dstat">${ico('monitor')}<div>Wirkt sofort, ohne FiveM-Neustart. HDR oder der Windows-Nachtmodus können die Werte überschreiben.</div></div>`);
  $('#dpStatus').innerHTML = st.join('');
}
let dpT = 0, dpBusy = false, dpAgain = false;
function dpPush() {
  clearTimeout(dpT);
  dpT = setTimeout(async () => {
    if (dpBusy) { dpAgain = true; return; }
    dpBusy = true;
    try { const r = await api('display.set', { cfg: DP.cfg }); DP.gammaOk = r.gammaOk; DP.vibOk = r.vibOk; if (r.keyOk === false) toast('Taste belegt', 'Andere Taste wählen', false); dpState(); }
    catch { }
    dpBusy = false; if (dpAgain) { dpAgain = false; dpPush(); }
  }, 70);
}
function dpToggle() { DP.cfg.on = !DP.cfg.on; dpState(); dpPush(); toast(DP.cfg.on ? 'Farbprofil an' : 'Farbprofil aus', DP.cfg.on && DP.cfg.onlyGame ? 'Wirkt, sobald FiveM im Vordergrund ist' : ''); }
function dpReset() { Object.assign(DP.cfg, DP_PRESETS.std[2]); dpUI(); dpPush(); }
// before and after in one picture: drag the line to compare
document.addEventListener('pointerdown', e => {
  const v = e.target.closest('#dpView'); if (!v || e.button) return;
  const set = ev => { const r = v.getBoundingClientRect(); v.style.setProperty('--split', Math.max(3, Math.min(97, (ev.clientX - r.left) / r.width * 100)).toFixed(2) + '%'); };
  set(e); v.setPointerCapture(e.pointerId); v.classList.add('drag');
  const up = () => { v.classList.remove('drag'); v.removeEventListener('pointermove', set); v.removeEventListener('pointerup', up); v.removeEventListener('pointercancel', up); };
  v.addEventListener('pointermove', set); v.addEventListener('pointerup', up); v.addEventListener('pointercancel', up);
});
async function dpUnlock() { const r = await api('display.unlock'); DP.unlocked = !!r.ok; dpState(); toast('Gamma-Bereich freigeschaltet', 'Starte den PC einmal neu'); }
document.addEventListener('click', e => {
  const p = e.target.closest('[data-dp]'); if (p) { Object.assign(DP.cfg, DP_PRESETS[p.dataset.dp][2]); if (p.dataset.dp !== 'std') DP.cfg.on = true; dpUI(); dpPush(); return; }
  const k = e.target.closest('[data-dk]'); if (k) { DP.cfg.key = +k.dataset.dk; dpUI(); dpPush(); return; }
  if (e.target.closest('#dpOnly')) { DP.cfg.onlyGame = !DP.cfg.onlyGame; dpState(); dpPush(); }
});
document.addEventListener('input', e => {
  const r = e.target.closest('[data-dr]'); if (!r) return;
  const k = r.dataset.dr, v = +r.value; DP.cfg[k] = v; DP.cfg.on = true;
  r.style.setProperty('--p', (v - r.min) / (r.max - r.min) * 100 + '%'); r.nextElementSibling.textContent = dpFmt(k, v);
  $$('.dpre').forEach(x => { const pv = DP_PRESETS[x.dataset.dp][2]; x.classList.toggle('on', Object.keys(pv).every(q => pv[q] === DP.cfg[q])); });
  dpState(); dpPush();
});
async function initFx(s) {
  const x = s.xhair || {};
  try { if (x.json) XH.cfg = { ...XH_DEF, ...JSON.parse(x.json) }; } catch { }
  Object.assign(XH, { on: !!x.on, onlyGame: x.onlyGame === true, key: x.key ?? 0x79, offsetX: x.offsetX || 0, offsetY: x.offsetY || 0 });
  xhUI(); xhPush();
  try {
    const d = await api('display.get');
    Object.assign(DP.cfg, d.cfg); DP.nvidia = d.nvidia; DP.unlocked = d.unlocked; DP.gammaOk = d.gammaOk;
  } catch { }
  dpUI();
}

/* ======================= F8 COMMANDS ======================= */
// Only commands from the official FiveM docs / production console whitelist.
// A click sends the command straight into FiveM's F8 console (OrbitTweak types it for you).
const F8 = [
  ['Anzeige', 'monitor', [
    { k: 'cl_drawfps', n: 'FPS-Anzeige', d: 'Zeigt die FPS oben in der Ecke.', t: 'bool', def: false },
    { k: 'cl_drawperf', n: 'Performance-Anzeige', d: 'FPS, Ping, Paketverlust, CPU/GPU-Last und GPU-Temperatur.', t: 'bool', def: false },
    { k: 'profile_reticuleSize', n: 'GTA-Fadenkreuz Größe', d: '0\u00a0=\u00a0kleinstmöglich. Ganz ausblenden kann es nur der Server.', t: 'range', min: 0, max: 10, def: 0 },
  ]],
  ['Kamera', 'eye', [
    { k: 'cam_disableCameraShake', n: 'Kamerawackeln aus', d: 'Kein Wackeln bei Explosionen und Treffern.', t: 'bool', def: false },
    { k: 'cam_enableHandbrakeCamera', n: 'Handbremsen-Kameraschwenk', d: 'Aus: Die Kamera schwenkt beim Driften nicht mehr mit.', t: 'bool', def: true },
    { k: 'cam_vehicleFirstPersonFOV', n: 'Fahrzeug-Ego-Sicht (FOV)', d: 'Sichtfeld in der Ego-Ansicht im Auto. -1\u00a0=\u00a0Standard.', t: 'range', min: -1, max: 130, def: -1 },
  ]],
  ['Performance', 'zap', [
    { k: 'str_maxVehicleTextureRes', n: 'Fahrzeug-Texturen begrenzen', d: 'Weniger Speicher, weniger Texture-Loss. Autos etwas unschärfer.', t: 'seg', o: [256, 512, 1024], def: 1024 },
    { k: 'voice_enableNoiseSuppression', n: 'Voice: Rauschunterdrückung', d: 'Filtert Hintergrundgeräusche im Voice-Chat.', t: 'bool', def: true },
  ]],
  ['Audio', 'activity', [
    { k: 'profile_musicVolumeInMp', n: 'Musik im Multiplayer', d: '0\u00a0=\u00a0aus, 10\u00a0=\u00a0100\u00a0%.', t: 'range', min: 0, max: 10, def: 10 },
    { k: 'profile_sfxVolume', n: 'Effekt-Lautstärke', d: 'Schüsse, Autos, Umgebung. 10\u00a0=\u00a0100\u00a0%.', t: 'range', min: 0, max: 10, def: 10 },
  ]],
];
const F8ALL = () => F8.flatMap(x => x[2]);
const f8Val = {}, f8St = {};
const f8Get = c => { const v = f8Val[c.k]; if (v == null) return c.def; return c.t === 'bool' ? v === 'true' || v === true : +v; };
const f8Cmd = (c, v = f8Get(c)) => `${c.k} ${v}`;
function f8Status(k) {
  const s = f8St[k];
  return s === 'sending' ? '<span class="chip a">Sende…</span>' : s === 'sent' ? '<span class="chip g">Live gesetzt</span>' : s === 'queued' ? '<span class="chip w">Beim Spielstart</span>' : s === 'error' ? '<span class="chip r">Fehler</span>' : '';
}
function renderF8() {
  $('#f8Auto').classList.toggle('on', S.settings.f8Auto !== false);
  $('#f8list').innerHTML = F8.map(([cat, icon, items]) => `<div class="card f8c"><div class="f8h">${ico(icon)}<h2>${cat}</h2><span class="f8n">${items.length} ${items.length === 1 ? 'Befehl' : 'Befehle'}</span></div>${items.map(c => {
    const v = f8Get(c);
    const ctl = c.t === 'bool' ? `<div class="tg ${v ? 'on' : ''}" data-f8b="${c.k}"></div>`
      : c.t === 'seg' ? `<div class="seg" data-f8="${c.k}">${c.o.map(o => `<button class="${v === o ? 'on' : ''}" data-v="${o}">${o}</button>`).join('')}</div>`
      : `<div class="rng"><input type="range" min="${c.min}" max="${c.max}" value="${v}" style="--p:${(v - c.min) / (c.max - c.min) * 100}%" data-f8r="${c.k}"><b>${v}</b></div>`;
    return `<div class="f8row t-${c.t}"><div class="top"><div><b>${c.n}</b><p>${c.d}</p></div>${ctl}</div><div class="f8meta"><span class="f8p">${ico('terminal')}</span><code id="f8c-${c.k}">${esc(f8Cmd(c))}</code><span class="f8s" id="f8s-${c.k}">${f8Status(c.k)}</span><button class="ib" title="Befehl kopieren" onclick="copyCmd($('#f8c-${c.k}').textContent)">${ico('copy')}</button></div></div>`;
  }).join('')}</div>`).join('');
}
async function f8Send(c, v) {
  f8Val[c.k] = String(v);
  const set = (st) => { f8St[c.k] = st; const el = $('#f8s-' + c.k); if (el) el.innerHTML = f8Status(c.k); };
  const code = $('#f8c-' + c.k); if (code) code.textContent = f8Cmd(c, v);
  set('sending');
  try {
    const r = await api('f8.send', { cmds: [f8Cmd(c, v)] });
    if (r.sent) { set('sent'); toast(`${c.n}: ${c.t === 'bool' ? (v ? 'an' : 'aus') : v}`, 'Live an FiveM gesendet'); }
    else if (r.error && r.error !== 'FiveM läuft nicht') { set('error'); toast('Nicht gesendet', r.error, false); }
    else { set('queued'); toast('Gespeichert', 'FiveM läuft nicht. Wird beim nächsten Spielstart automatisch gesetzt.'); }
  } catch (e) { set('error'); toast('Nicht gesendet', e.message, false); }
}
window.f8Quick = async (k, v) => { const c = F8ALL().find(x => x.k === k); await f8Send(c, v); if (curPage === 'f8') renderF8(); };
document.addEventListener('click', e => {
  const t = e.target.closest('[data-f8b]');
  if (t) { const c = F8ALL().find(x => x.k === t.dataset.f8b), v = !f8Get(c); t.classList.toggle('on', v); f8Send(c, v); return; }
  const b = e.target.closest('[data-f8] button');
  if (b) { const c = F8ALL().find(x => x.k === b.parentElement.dataset.f8); b.parentElement.querySelectorAll('button').forEach(x => x.classList.toggle('on', x === b)); f8Send(c, +b.dataset.v); return; }
  if (e.target.closest('#f8Auto')) { S.settings.f8Auto = S.settings.f8Auto === false; api('settings.set', { key: 'f8Auto', value: S.settings.f8Auto }); renderF8(); }
});
document.addEventListener('input', e => {
  const r = e.target.closest('[data-f8r]'); if (!r) return;
  const c = F8ALL().find(x => x.k === r.dataset.f8r);
  r.style.setProperty('--p', (r.value - r.min) / (r.max - r.min) * 100 + '%'); r.nextElementSibling.textContent = r.value;
  $('#f8c-' + c.k).textContent = f8Cmd(c, +r.value);
});
document.addEventListener('change', e => { const r = e.target.closest('[data-f8r]'); if (r) f8Send(F8ALL().find(x => x.k === r.dataset.f8r), +r.value); });
async function loadF8() { try { Object.assign(f8Val, await api('f8.values')); } catch { } }

/* ======================= LIVE DISPLAY MODE ======================= */
async function gwRefresh() {
  try {
    const g = await api('gw.info'), st = $('#gwSt');
    // mode as a quiet value with a status dot: borderless is the goal, a window works, exclusive full screen hides overlays
    const m = !g.found ? ['off', 'Nicht gestartet', 'FiveM läuft gerade nicht'] : g.mode === 'windowed' ? ['warn', 'Fenster', 'Mit Rand, Alt-Tab etwas langsamer']
      : g.mode === 'borderless' ? ['ok', 'Randlos', 'Ideal für Fadenkreuz und Alt-Tab'] : ['warn', 'Vollbild', 'Overlays sind hier unsichtbar'];
    if (st) st.dataset.st = m[0];
    $('#gwState').textContent = m[1]; $('#gwStateS').textContent = m[2];
    $('#gwRes').textContent = g.found && g.w ? `${g.w}×${g.h}` : '—';
    $('#gwAuto').classList.toggle('on', !!S.settings.autoBorderless);
  } catch { }
}
async function gwSet(mode) {
  const r = await api('gw.' + mode).catch(e => ({ error: e.message }));
  if (r.error) toast('Geht gerade nicht', r.error, false);
  else toast(mode === 'borderless' ? 'Randloses Vollbild aktiv' : 'Fenstermodus aktiv', 'Sofort, ohne Neustart');
  gwRefresh();
}
document.addEventListener('click', e => {
  if (!e.target.closest('#gwAuto')) return;
  S.settings.autoBorderless = !S.settings.autoBorderless;
  api('settings.set', { key: 'autoBorderless', value: S.settings.autoBorderless });
  gwRefresh();
  if (S.settings.autoBorderless) toast('Automatisch randlos', 'Sobald FiveM im Fenstermodus läuft');
});
setInterval(() => { if (curPage === 'gfx') gwRefresh(); }, 2500);

/* ======================= FIVEM LOOK ======================= */
function sceneClone(suffix) {
  const src = $('#xhScene .gscene'); if (!src) return '';
  return src.outerHTML.replace(/\b(gsky|gsun|groad)\b/g, `$1${suffix}`);
}
function timeOfDay(d = new Date()) { const h = d.getHours(); return h >= 6 && h < 17 ? 'day' : h >= 17 && h < 21 ? 'dusk' : 'night'; }
function banner() {
  const d = new Date(), h = d.getHours(), tod = timeOfDay(d);
  $('#bnScene').className = 'bn-scene';
  $('#greet').textContent = h < 5 ? 'Gute Nacht' : h < 11 ? 'Guten Morgen' : h < 17 ? 'Guten Tag' : h < 22 ? 'Guten Abend' : 'Gute Nacht';
  $('#bnTime').textContent = `Los Santos, ${d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}`;
}
function initLook() {
  $('#bnScene').innerHTML = `<div class="own-bg" style="background-image:url('${LA.day}')"></div>`; $('#spScene').innerHTML = $('#bnScene').innerHTML;
  banner(); setInterval(banner, 30000);
}
function passed(title, sub) {
  const p = $('#passed'); $('#paT').textContent = title; $('#paS').textContent = sub || '';
  p.classList.remove('show'); void p.offsetWidth; p.classList.add('show');
  clearTimeout(p._t); p._t = setTimeout(() => p.classList.remove('show'), 3300);
}
window.passed = passed;

/* ======================= MINIMAP ======================= */
const MM_STYLES = [
  ['round', 'Rund', '<circle cx="12" cy="12" r="7"/>'],
  ['square', 'Quadrat', '<rect x="5" y="5" width="14" height="14" rx="3"/>'],
  ['frame', 'Rahmen', '<rect x="3" y="6" width="18" height="12" rx="2"/>'],
  ['glow', 'Neon', '<rect x="3" y="6" width="18" height="12" rx="2"/><rect x="1" y="4" width="22" height="16" rx="3" opacity=".4"/>'],
  ['corners', 'Ecken', '<path d="M3 9V6h4M17 6h4v3M21 15v3h-4M7 18H3v-3"/>'],
];
const MM = { cfg: { style: 'round', color: '#FF7A3D', thick: 3, mask: 92, glow: true }, on: false, x: 0, y: 0, w: 0, h: 0, res: { w: 1920, h: 1080 }, align: false, P: 26 };
function mmDefaults(push = true) {
  const H = MM.res.h;
  Object.assign(MM, { x: Math.round(H * .026), y: Math.round(H * .045), w: Math.round(H * .243), h: Math.round(H * .157) });
  if (push) { mmUI(); mmPush(); }
}
function mmMapMock(x, w, h) {
  x.fillStyle = '#86977A'; x.fillRect(0, 0, w, h);
  x.fillStyle = '#6E8FB0'; x.beginPath(); x.moveTo(w * .7, 0); x.lineTo(w, 0); x.lineTo(w, h * .55); x.quadraticCurveTo(w * .8, h * .4, w * .7, 0); x.fill();
  x.strokeStyle = '#D9D4C5'; x.lineWidth = Math.max(2, h / 40);
  [[0, h * .45, w, h * .5], [w * .3, 0, w * .38, h], [0, h * .9, w * .8, h * .2], [w * .62, h, w * .7, h * .35]].forEach(([a, b, c, d]) => { x.beginPath(); x.moveTo(a, b); x.lineTo(c, d); x.stroke(); });
  x.fillStyle = '#fff'; x.beginPath(); x.moveTo(w / 2, h / 2 - 7); x.lineTo(w / 2 + 5, h / 2 + 6); x.lineTo(w / 2, h / 2 + 3); x.lineTo(w / 2 - 5, h / 2 + 6); x.fill();
}
function mmDraw(align = MM.align) {
  const { P, w, h } = MM, c = MM.cfg, cv = document.createElement('canvas'); cv.width = w + 2 * P; cv.height = h + 2 * P;
  const x = cv.getContext('2d'), mask = `rgba(9,9,11,${c.mask / 100})`, t = c.thick;
  const glow = () => { if (c.glow || c.style === 'glow') { x.shadowColor = c.color; x.shadowBlur = c.style === 'glow' ? 18 : 10; } };
  const rr = (X, Y, W, H, r) => { x.beginPath(); x.roundRect(X, Y, W, H, r); };
  if (c.style === 'round' || c.style === 'square') {
    const s = h, cx = P + w / 2, cy = P + h / 2;
    x.beginPath(); x.rect(P, P, w, h);
    if (c.style === 'round') x.arc(cx, cy, s / 2 - t / 2, 0, Math.PI * 2, true); else x.roundRect(cx - s / 2 + t / 2, P + t / 2, s - t, s - t, 12);
    x.fillStyle = mask; x.fill('evenodd');
    glow(); x.strokeStyle = c.color; x.lineWidth = t; x.beginPath();
    if (c.style === 'round') x.arc(cx, cy, s / 2 - t / 2, 0, Math.PI * 2); else x.roundRect(cx - s / 2 + t / 2, P + t / 2, s - t, s - t, 12);
    x.stroke();
  } else if (c.style === 'corners') {
    glow(); x.strokeStyle = c.color; x.lineWidth = t; x.lineCap = 'round'; const L = Math.min(w, h) * .22;
    [[P, P + L, P, P, P + L, P], [P + w - L, P, P + w, P, P + w, P + L], [P + w, P + h - L, P + w, P + h, P + w - L, P + h], [P + L, P + h, P, P + h, P, P + h - L]]
      .forEach(([a, b, c2, d, e, f]) => { x.beginPath(); x.moveTo(a, b); x.lineTo(c2, d); x.lineTo(e, f); x.stroke(); });
  } else {
    glow(); x.strokeStyle = c.color; x.lineWidth = t; rr(P - t / 2, P - t / 2, w + t, h + t, 8); x.stroke();
    if (c.style === 'glow') { x.shadowBlur = 0; x.globalAlpha = .45; x.lineWidth = 1; rr(P - t - 3, P - t - 3, w + 2 * t + 6, h + 2 * t + 6, 11); x.stroke(); x.globalAlpha = 1; }
  }
  if (align) { x.shadowBlur = 0; x.fillStyle = 'rgba(255,0,200,.16)'; x.fillRect(P, P, w, h); x.setLineDash([6, 4]); x.strokeStyle = '#FF2BD6'; x.lineWidth = 2; x.strokeRect(P + 1, P + 1, w - 2, h - 2); }
  return cv;
}
function mmRender() {
  const img = mmDraw(), cv = $('#mmCanvas'), P = MM.P;
  cv.width = img.width; cv.height = img.height;
  const x = cv.getContext('2d'); x.save(); x.translate(P, P); mmMapMock(x, MM.w, MM.h); x.restore(); x.drawImage(img, 0, 0);
  cv.style.width = Math.min(img.width * 1.4, 520) + 'px';
}
let mmT = 0;
function mmPush() {
  mmRender(); clearTimeout(mmT);
  mmT = setTimeout(async () => {
    const png = mmDraw().toDataURL('image/png').split(',')[1];
    const r = await api('minimap.set', { on: MM.on, x: MM.x - MM.P, y: MM.y - MM.P, png, json: JSON.stringify({ ...MM.cfg, x: MM.x, y: MM.y, w: MM.w, h: MM.h }) }).catch(() => null);
    if (r && r.client && r.client.h) { MM.res = r.client; $('#mmRes').textContent = `${r.client.w}×${r.client.h}`; if (MM.fresh) { MM.fresh = false; mmDefaults(); } }
  }, 120);
}
function mmUI() {
  $('#mmStyles').innerHTML = MM_STYLES.map(([k, n, svg]) => `<button class="xst ${MM.cfg.style === k ? 'on' : ''}" data-mms="${k}"><svg viewBox="0 0 24 24">${svg}</svg>${n}</button>`).join('');
  const cols = [getComputedStyle(document.documentElement).getPropertyValue('--a').trim().toUpperCase(), '#FFFFFF', '#3DE0FF', '#3DFF8B', '#FFE03D', '#FF3B5C', '#B78CFF'];
  $('#mmColors').innerHTML = [...new Set(cols)].map(c => `<button class="xcol ${MM.cfg.color.toUpperCase() === c ? 'on' : ''}" data-mmc="${c}" style="background:${c}"></button>`).join('') + `<label class="xcustom" title="Eigene Farbe"><input type="color" id="mmPick" value="${MM.cfg.color}"></label>`;
  const H = MM.res.h, sl = [['thick', 'Dicke', 1, 8, MM.cfg.thick], ['mask', 'Abdeckung', 40, 100, MM.cfg.mask, '%'], ['x', 'Links', 0, Math.round(H * .2), MM.x], ['y', 'Unten', 0, Math.round(H * .2), MM.y], ['w', 'Breite', 100, Math.round(H * .5), MM.w], ['h', 'Höhe', 60, Math.round(H * .4), MM.h]];
  $('#mmSliders').innerHTML = `<div class="xs tgl"><span>Leuchten</span><div class="tg ${MM.cfg.glow ? 'on' : ''}" data-mmb="glow"></div></div>` +
    sl.map(([k, n, mi, ma, v, u = '']) => `<div class="xs"><span>${n}</span><input type="range" min="${mi}" max="${ma}" value="${v}" style="--p:${(v - mi) / (ma - mi) * 100}%" data-mmr="${k}"><b>${v}${u}</b></div>`).join('');
  $('#mmAlign').classList.toggle('on', MM.align);
  $('#mmMain').querySelector('span').textContent = MM.on ? 'Ausschalten' : 'Einschalten';
  $('#mmMain').classList.toggle('p', !MM.on);
  $('#navMm').classList.toggle('hidden', !MM.on);
  mmRender();
}
function mmToggle() { MM.on = !MM.on; mmUI(); mmPush(); toast(MM.on ? 'Minimap-Overlay an' : 'Minimap-Overlay aus', MM.on ? 'Erscheint, sobald FiveM vorne ist · Server-Regeln beachten' : ''); }
window.mmToggle = mmToggle; window.mmDefaults = mmDefaults;
document.addEventListener('click', e => {
  const s = e.target.closest('[data-mms]'); if (s) { MM.cfg.style = s.dataset.mms; mmUI(); mmPush(); return; }
  const c = e.target.closest('[data-mmc]'); if (c) { MM.cfg.color = c.dataset.mmc; mmUI(); mmPush(); return; }
  const b = e.target.closest('[data-mmb]'); if (b) { MM.cfg[b.dataset.mmb] = !MM.cfg[b.dataset.mmb]; mmUI(); mmPush(); return; }
  if (e.target.closest('#mmAlign')) { MM.align = !MM.align; if (MM.align && !MM.on) { MM.on = true; } api('minimap.preview', { on: MM.align }); mmUI(); mmPush(); if (MM.align) toast('Ausrichten-Modus', 'Starte FiveM und schieb den pinken Rahmen mit den Reglern genau auf die Minimap'); }
});
document.addEventListener('input', e => {
  const r = e.target.closest('[data-mmr]');
  if (r) { const k = r.dataset.mmr, v = +r.value; if (k in MM.cfg) MM.cfg[k] = v; else MM[k] = v; r.style.setProperty('--p', (v - r.min) / (r.max - r.min) * 100 + '%'); r.nextElementSibling.textContent = v + (k === 'mask' ? '%' : ''); mmPush(); return; }
  if (e.target.id === 'mmPick') { MM.cfg.color = e.target.value.toUpperCase(); mmPush(); }
});
/* ======================= PICTURE OVER THE MINIMAP (overlay, 2.21) ======================= */
// The user's own picture is laid over the game's minimap: an overlay window like the crosshair, positioned from the
// bottom left corner of the game picture. Nothing in the game files is changed. Sizes are pixels of the game picture.
// auto: position and size were not set by hand yet, so they follow the size of the game picture
const MMO = { kind: 'ov', on: false, path: '', name: '', iw: 0, ih: 0, img: null, x: 0, y: 0, w: 0, h: 0, auto: true, fit: 'cover', shape: 'rect', op: 100, cw: 1920, ch: 1080, prev: false, t: 0 };
// where the minimap usually sits: bottom left, a quarter of the picture height wide
function mmoDefaults() { const H = MMO.ch; Object.assign(MMO, { x: Math.round(H * .025), y: Math.round(H * .037), w: Math.round(H / 4), h: Math.round(H / 6) }); }
function mmoPaint(x, dx, dy, w, h) {
  if (!MMO.img) return;
  x.save(); x.globalAlpha = MMO.op / 100; x.imageSmoothingQuality = 'high';
  x.beginPath();
  if (MMO.shape === 'circle') x.ellipse(dx + w / 2, dy + h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
  else if (MMO.shape === 'round') x.roundRect(dx, dy, w, h, Math.min(w, h) * .12);
  else x.rect(dx, dy, w, h);
  x.clip();
  const iw = MMO.img.width, ih = MMO.img.height;
  if (MMO.fit === 'stretch') x.drawImage(MMO.img, dx, dy, w, h);
  else { const s = MMO.fit === 'contain' ? Math.min(w / iw, h / ih) : Math.max(w / iw, h / ih), tw = iw * s, th = ih * s; x.drawImage(MMO.img, dx + (w - tw) / 2, dy + (h - th) / 2, tw, th); }
  x.restore();
}
/** The overlay picture exactly as it is shown in the game (base64 PNG), or '' without a picture. */
function mmoPng() {
  if (!MMO.img || MMO.w < 8 || MMO.h < 8) return '';
  const c = document.createElement('canvas'); c.width = MMO.w; c.height = MMO.h;
  mmoPaint(c.getContext('2d'), 0, 0, MMO.w, MMO.h);
  return c.toDataURL('image/png').split(',')[1];
}
/** Sends position and (if it changed) the picture to the app; a moment after the last change while sliders move. */
function mmoPush(pic = true, wait = 120) {
  MMO.pic = MMO.pic || pic; clearTimeout(MMO.t);
  MMO.t = setTimeout(async () => {
    const png = MMO.pic ? mmoPng() : ''; MMO.pic = false;
    const json = JSON.stringify({ path: MMO.path, name: MMO.name, x: MMO.x, y: MMO.y, w: MMO.w, h: MMO.h, auto: MMO.auto, fit: MMO.fit, shape: MMO.shape, op: MMO.op });
    const r = await api('minimap.set', { on: MMO.on && !!MMO.img, x: MMO.x, y: MMO.y, json, png }).catch(() => null);
    if (r && r.client && r.client.h > 200 && (r.client.w !== MMO.cw || r.client.h !== MMO.ch)) {
      MMO.cw = r.client.w; MMO.ch = r.client.h;
      if (MMO.auto) { mmoDefaults(); mmoUI(); mmoPush(true, 0); } else mmoUI();   // another picture size: lay it on the minimap again
    }
  }, wait);
}
function mmoLoad(i) {
  return new Promise(res => {
    Object.assign(MMO, { path: i.path, name: i.name, iw: i.w, ih: i.h, img: null });
    const im = new Image(); im.onload = () => { MMO.img = im; res(true); }; im.onerror = () => res(false); im.src = i.preview;
  });
}
function mmoPreview(on) { MMO.prev = on; api('minimap.preview', { on }).catch(() => { }); mmoUI(); }
function mmoUI() {
  if (!$('#mmoPick')) return;
  $$('#mmKind button').forEach(b => b.classList.toggle('on', b.dataset.v === MMO.kind));
  $('#mmOvPane').classList.toggle('hidden', MMO.kind !== 'ov'); $('#mmModPane').classList.toggle('hidden', MMO.kind !== 'mod');
  const has = !!MMO.img;
  $('#mmoName').textContent = MMO.path ? MMO.name : 'Bild auswählen';
  $('#mmoInfo').textContent = MMO.path ? `${MMO.iw} × ${MMO.ih} Pixel` : 'PNG, JPG oder WebP';
  const th = $('#mmoTh'); th.style.backgroundImage = has ? `url('${MMO.img.src}')` : ''; th.classList.toggle('has', has);
  $('#mmoOn').classList.toggle('on', MMO.on && has);
  const chip = $('#mmoChip'); chip.className = 'chip' + (MMO.on && has ? ' g' : ''); chip.textContent = MMO.on && has ? 'Aktiv' : 'Aus';
  const rng = (id, v, max) => { const el = $(id); if (max) el.max = max; el.value = v; el.style.setProperty('--p', (v - el.min) / (el.max - el.min) * 100 + '%'); $(id + 'V').textContent = v + (id === '#mmoOp' ? '%' : ' px'); };
  rng('#mmoX', MMO.x, Math.round(MMO.cw / 2)); rng('#mmoY', MMO.y, Math.round(MMO.ch / 2));
  rng('#mmoW', MMO.w, Math.round(MMO.cw / 2)); rng('#mmoH', MMO.h, Math.round(MMO.ch / 2)); rng('#mmoOp', MMO.op);
  $$('#mmoFit button').forEach(b => b.classList.toggle('on', b.dataset.v === MMO.fit));
  $$('#mmoShape button').forEach(b => b.classList.toggle('on', b.dataset.v === MMO.shape));
  $('#mmoPrev').classList.toggle('p', MMO.prev); $('#mmoPrev').querySelector('span').textContent = MMO.prev ? 'Vorschau auf dem Bildschirm aus' : 'Auf dem Bildschirm zeigen';
  $('#mmoHint').innerHTML = !has ? 'Wähl links ein Bild aus. Danach siehst du hier, wo es im Spiel liegt.'
    : `Das Bild liegt ${MMO.x} px vom linken und ${MMO.y} px vom unteren Rand, bei einem Spielbild von ${MMO.cw} × ${MMO.ch}. Passt es nicht genau auf deine Minimap, starte FiveM, schalte „Auf dem Bildschirm zeigen“ ein und schieb es mit den Reglern zurecht.`;
  if ($('#mtMm') && MMO.on && has) $('#mtMm').textContent = 'Overlay aktiv';
  mmoDraw();
}
/** Preview: the bottom left corner of the game picture with a stand-in minimap and the picture on top. */
function mmoDraw() {
  const cv = $('#mmoCv'); if (!cv) return;
  const x = cv.getContext('2d'), W = cv.width, H = cv.height;
  // shown part of the game picture: wide enough for the picture plus some room
  const vw = Math.max(MMO.ch * .62, (MMO.x + MMO.w) * 1.25), vh = vw * H / W, k = W / vw;
  const g = x.createLinearGradient(0, 0, 0, H); g.addColorStop(0, '#5A6B78'); g.addColorStop(.55, '#4B4F4A'); g.addColorStop(1, '#2F3130');
  x.fillStyle = g; x.fillRect(0, 0, W, H);
  x.strokeStyle = 'rgba(255,255,255,.06)'; x.lineWidth = 2; for (let i = 1; i < 6; i++) { x.beginPath(); x.moveTo(0, H * (.5 + i * .09)); x.lineTo(W, H * (.42 + i * .1)); x.stroke(); }
  // stand-in minimap where GTA draws it
  const d = { x: MMO.ch * .025, y: MMO.ch * .037, w: MMO.ch / 4, h: MMO.ch / 6 };
  const bx = d.x * k, by = H - (d.y + d.h) * k, bw = d.w * k, bh = d.h * k;
  x.save(); x.beginPath(); x.rect(bx, by, bw, bh); x.clip(); x.translate(bx, by); mmMapMock(x, bw, bh); x.restore();
  x.strokeStyle = 'rgba(0,0,0,.6)'; x.lineWidth = 2; x.strokeRect(bx, by, bw, bh);
  x.fillStyle = '#5BBF57'; x.fillRect(bx, by + bh + 4, bw * .5 - 2, 6); x.fillStyle = '#4A9BD8'; x.fillRect(bx + bw * .5 + 2, by + bh + 4, bw * .5 - 2, 6);
  if (MMO.img) mmoPaint(x, MMO.x * k, H - (MMO.y + MMO.h) * k, MMO.w * k, MMO.h * k);
  else { x.fillStyle = 'rgba(255,255,255,.6)'; x.font = '600 22px Inter, sans-serif'; x.textAlign = 'center'; x.fillText('Noch kein Bild gewählt', W / 2, H * .3); }
}
document.addEventListener('click', async e => {
  const kind = e.target.closest('#mmKind button'); if (kind) { MMO.kind = kind.dataset.v; if (MMO.kind !== 'ov' && MMO.prev) mmoPreview(false); mmoUI(); return; }
  if (!e.target.closest('#mmOvPane')) return;
  if (e.target.closest('#mmoPick')) {
    const i = await api('minimap.pick').catch(er => ({ error: er.message }));
    if (i.cancelled) return;
    if (i.error) return toast('Bild nicht geladen', i.error, false);
    if (!await mmoLoad(i)) return toast('Bild nicht geladen', 'Die Datei konnte nicht gelesen werden', false);
    MMO.on = true; mmoUI(); mmoPush(true, 0);
    toast('Minimap-Bild aktiv', 'Erscheint, sobald FiveM vorne ist · Server-Regeln beachten');
    return;
  }
  if (e.target.closest('#mmoOn')) {
    if (!MMO.img) return toast('Erst ein Bild auswählen', '', false);
    MMO.on = !MMO.on; mmoUI(); mmoPush(true, 0);
    toast(MMO.on ? 'Minimap-Bild an' : 'Minimap-Bild aus', MMO.on ? 'Erscheint, sobald FiveM vorne ist' : ''); return;
  }
  if (e.target.closest('#mmoAuto')) { MMO.auto = true; mmoDefaults(); mmoUI(); mmoPush(true, 0); return; }
  if (e.target.closest('#mmoPrev')) {
    if (!MMO.img) return toast('Erst ein Bild auswählen', '', false);
    mmoPreview(!MMO.prev); if (MMO.prev) { mmoPush(true, 0); toast('Vorschau an', 'Das Bild ist jetzt auch zu sehen, wenn FiveM nicht vorne ist'); } return;
  }
  const f = e.target.closest('#mmoFit button'); if (f) { MMO.fit = f.dataset.v; mmoUI(); mmoPush(); return; }
  const sh = e.target.closest('#mmoShape button'); if (sh) { MMO.shape = sh.dataset.v; mmoUI(); mmoPush(); return; }
});
document.addEventListener('input', e => {
  const k = { mmoX: 'x', mmoY: 'y', mmoW: 'w', mmoH: 'h', mmoOp: 'op' }[e.target.id]; if (!k) return;
  MMO[k] = +e.target.value; if (k !== 'op') MMO.auto = false;
  mmoUI(); mmoPush(k !== 'x' && k !== 'y');   // moving needs no new picture
});
// on start: bring back the picture that was set up (an old frame overlay from before 2.9 is switched off)
async function initMinimap(s) {
  const m = s.minimap || {}; let c = null; try { c = m.json ? JSON.parse(m.json) : null; } catch { }
  if (!c || !c.path) { if (m.on) api('minimap.set', { on: false, x: 0, y: 0 }).catch(() => { }); mmoDefaults(); return; }
  Object.assign(MMO, { x: c.x | 0, y: c.y | 0, w: Math.max(60, c.w | 0), h: Math.max(40, c.h | 0), auto: !!c.auto, fit: ['cover', 'contain', 'stretch'].includes(c.fit) ? c.fit : 'cover', shape: ['rect', 'round', 'circle'].includes(c.shape) ? c.shape : 'rect', op: Math.min(100, Math.max(20, c.op | 0 || 100)), on: !!m.on, path: c.path, name: c.name || '' });
  const i = await api('minimap.info', { path: c.path }).catch(() => null);
  if (!i || i.error || !await mmoLoad(i)) { MMO.on = false; if (m.on) api('minimap.set', { on: false, x: MMO.x, y: MMO.y }).catch(() => { }); mmoUI(); return; }   // the picture file is gone
  mmoUI(); mmoPush(true, 0);
}

/* ======================= CLIPPER ======================= */
const CL_RECS = {
  orbit: ['OrbitTweak', 'Eingebaut', 'Empfohlen: OrbitTweak nimmt selbst auf, du brauchst kein anderes Programm. Clips landen in Videos\\OrbitTweak.'],
  nvidia: ['NVIDIA', 'Alt+F10', 'In der NVIDIA App (Alt+Z) unter Aufnahme „Instant Replay“ einschalten. Clips landen in Videos.'],
  gamebar: ['Game Bar', 'Win+Alt+G', 'Windows Game Bar mit Hintergrundaufnahme. Clips landen in Videos\\Captures.'],
  amd: ['AMD', 'Strg+Shift+S', 'In AMD Adrenalin „Instant Replay“ aktivieren.'],
  obs: ['OBS', 'Strg+Alt+F12', 'In OBS den Replay-Puffer starten und unter Einstellungen → Hotkeys „Wiederholung speichern“ auf Strg+Alt+F12 legen.'],
};
const CL_WORDS = 'KILLED, KILL, GETÖTET, ELIMINATED, ELIMINIERT, ERLEDIGT';
const CL = { key: 0x78, recorder: 'orbit', flash: true, auto: false, sens: 2, kills: false, detect: 'mark', words: CL_WORDS, net: true, banner: true, autoHighlight: false, highlightTail: 8, shareMb: 10, deaths: false, name: '', area: 'auto', before: 20, after: 5, quality: 'std', sound: true, montageTitle: '', montageIntro: true, montageCounter: true };
// how kills are noticed: [value, name, plain explanation, icon, tag]
const CL_DETECT = [['mark', 'Am Fadenkreuz', 'Liest die Kill-Meldung in der Bildmitte und das rote X. Kein Name, kein Kill-Feed nötig.', 'crosshair', 'Empfohlen'],
  ['feed', 'Im Kill-Feed', 'Sucht deinen Ingame-Namen im Kill-Feed am Bildrand. Für Server, die Namen als Text zeigen.', 'eye', '']];
const KF_AREAS = [['auto', 'Automatisch'], ['tr', 'Oben rechts'], ['tl', 'Oben links'], ['r', 'Rechts Mitte'], ['tc', 'Oben Mitte']];
const CL_QUAL = [['low', 'Leicht', '720p · 30 FPS'], ['std', 'Standard', '1080p · 60 FPS'], ['high', 'Hoch', 'bis 1440p · 60 FPS']];
let clSt = null; // live state of the built-in recorder
function clFlashPng() {
  const cv = document.createElement('canvas'); cv.width = 380; cv.height = 58; const x = cv.getContext('2d');
  x.fillStyle = 'rgba(10,10,12,.9)'; x.beginPath(); x.roundRect(0, 0, 380, 58, 12); x.fill();
  const a = getComputedStyle(document.documentElement).getPropertyValue('--a').trim();
  x.fillStyle = a; x.fillRect(0, 10, 4, 38);
  x.fillStyle = '#FF3B3B'; x.beginPath(); x.arc(30, 29, 7, 0, Math.PI * 2); x.fill();
  x.fillStyle = '#fff'; x.font = '700 18px Geist, Inter, sans-serif'; x.fillText('CLIP GESPEICHERT', 50, 27);
  x.fillStyle = '#A1A1AA'; x.font = '500 12px Inter, sans-serif'; x.fillText(CL.recorder === 'orbit' ? 'OrbitTweak Clipper' : `OrbitTweak · ${CL_RECS[CL.recorder][0]}`, 50, 44);
  return cv.toDataURL('image/png').split(',')[1];
}
async function clipPush() {
  const accent = getComputedStyle(document.documentElement).getPropertyValue('--a').trim();
  const r = await api('clip.cfg', { key: CL.key, recorder: CL.recorder, flash: CL.flash, auto: CL.auto, sens: CL.sens, kills: CL.kills, detect: CL.detect, words: CL.words || '', net: CL.net, ai: CL.ai !== false, hud: CL.hud !== false, hudTime: CL.hudTime !== false, hudClock: !!CL.hudClock, banner: CL.banner !== false, autoHighlight: !!CL.autoHighlight, highlightTail: CL.highlightTail ? 8 : 0, shareMb: CL.shareMb || 10, accent: /^#[0-9a-f]{6}$/i.test(accent) ? accent : '', deaths: CL.deaths, name: CL.name, area: CL.area, before: CL.before, after: CL.after, quality: CL.quality, sound: CL.sound, montageTitle: (CL.montageTitle || '').trim(), montageIntro: CL.montageIntro !== false, montageCounter: CL.montageCounter !== false, png: clFlashPng() }).catch(() => null);
  if (r && r.keyOk === false) toast('Taste belegt', 'Andere Clip-Taste wählen', false);
  // the app tidies the word list (and falls back to the usual words when it is empty)
  if (r && r.words) { CL.words = r.words; if (document.activeElement !== $('#clWords')) $('#clWords').value = CL.words; }
  clipStatus();
}
function clipUI() {
  if (!CL_RECS[CL.recorder]) CL.recorder = 'orbit';
  const own = CL.recorder === 'orbit';
  // each recorder: its name, and the keys that save a clip with it as small key caps
  const caps = c => /\+/.test(c) ? c.split('+').map(k => `<kbd>${esc(k)}</kbd>`).join('') : `<em>${esc(c)}</em>`;
  $('#clRecs').innerHTML = Object.entries(CL_RECS).map(([k, [n, combo]]) => `<button class="cl-rec ${CL.recorder === k ? 'on' : ''}" data-rec="${k}"><b>${n}${ico('check', 'i cr-ck')}</b><span class="cr-k">${caps(combo)}</span></button>`).join('');
  $('#clRecTxt').innerHTML = esc(CL_RECS[CL.recorder][2]) + (CL.recorder === 'gamebar' ? `<br><button class="btn sm mt8" onclick="clipGameBar()">${ico('zap')}Hintergrundaufnahme einschalten</button>` : '');
  $('#clOrbit').classList.toggle('hidden', !own); $('#clOrbitNote').classList.toggle('hidden', !own); $('#clExtNote').classList.toggle('hidden', own);
  $('#clQual').innerHTML = CL_QUAL.map(([v, n, d]) => `<button class="${CL.quality === v ? 'on' : ''}" data-clq="${v}"><b>${n}</b><span>${d}</span></button>`).join('');
  // the clip as a strip: seconds before the kill, the kill, seconds after
  const tl = $('#clTl'), len = CL.before + CL.after;
  if (tl) { tl.style.setProperty('--k', (CL.before / Math.max(1, len)).toFixed(3)); tl.style.setProperty('--n', Math.max(1, len)); }
  if (tl) tl.innerHTML = `<div class="clt-h"><span>Länge pro Clip</span><b>${len}<small>s</small></b></div>
    <div class="clt-bar"><i class="clt-b"></i><i class="clt-k"></i><i class="clt-a"></i></div>
    <div class="clt-l"><span>${CL.before} s davor</span><span class="clt-km">Kill</span><span>${CL.after ? CL.after + ' s danach' : 'endet beim Kill'}</span></div>`;
  $('#clBefore').innerHTML = [10, 15, 20, 30, 45].map(v => `<button class="${CL.before === v ? 'on' : ''}" data-clb="${v}">${v} s</button>`).join('');
  $('#clAfter').innerHTML = [0, 3, 5, 8, 10].map(v => `<button class="${CL.after === v ? 'on' : ''}" data-cla="${v}">${v} s</button>`).join('');
  $('#clSound').classList.toggle('on', CL.sound);
  $('#clKey').innerHTML = KEYS.map(([n, v]) => `<button class="${CL.key === v ? 'on' : ''}" data-clk="${v}">${n}</button>`).join('');
  $('#clFlash').classList.toggle('on', CL.flash);
  $('#clNet').classList.toggle('on', CL.net !== false);
  $('#clBanner').classList.toggle('on', CL.banner !== false);
  $('#clAi').classList.toggle('on', CL.ai !== false); $('#clAiKeyRow').classList.toggle('hidden', CL.ai === false);
  $('#clHud').classList.toggle('on', CL.hud !== false);
  $$('#clHudX button').forEach(b => b.classList.toggle('on', b.dataset.hx === 'hudTime' ? CL.hudTime !== false : !!CL.hudClock));
  $('#hlAuto').classList.toggle('on', !!CL.autoHighlight);
  $('#clShareMb').innerHTML = [[10, '10 MB'], [50, '50 MB'], [500, '500 MB']].map(([v, n]) => `<button class="${(CL.shareMb || 10) === v ? 'on' : ''}" data-shmb="${v}">${n}</button>`).join('');
  HL.tail = CL.highlightTail ? 8 : 0; hlUI();
  if (document.activeElement !== $('#clWords')) $('#clWords').value = CL.words || CL_WORDS;
  // the "how many shots make a firefight" setting also counts for the safety net of the kill clipper
  $('#clAuto').classList.toggle('on', CL.auto); $('#clAutoOpts').classList.toggle('off', !CL.auto && !(CL.kills && CL.detect !== 'feed' && CL.net !== false));
  $('#clKills').classList.toggle('on', CL.kills); $('#clKillOpts').classList.toggle('off', !CL.kills);
  $('#clDeaths').classList.toggle('on', CL.deaths);
  if (document.activeElement !== $('#clName')) $('#clName').value = CL.name || '';
  if (document.activeElement !== $('#kcName')) $('#kcName').value = CL.name || '';
  $('#clArea').innerHTML = KF_AREAS.map(([v, n]) => `<button class="${CL.area === v ? 'on' : ''}" data-kfa="${v}">${n}</button>`).join('');
  $('#clSens').innerHTML = [[1, 'Nur große Gefechte'], [2, 'Normal'], [3, 'Jeder Schusswechsel']].map(([v, n]) => `<button class="${CL.sens === v ? 'on' : ''}" data-cls="${v}">${n}</button>`).join('');
  // how kills are noticed: red X on the crosshair (default, nothing to set up) or the kill feed (needs the name)
  if (CL.detect !== 'feed') CL.detect = 'mark';
  const feed = CL.detect === 'feed';
  $('#clDetect').innerHTML = CL_DETECT.map(([v, n, d, ic, tag]) => `<button class="ke-m ${CL.detect === v ? 'on' : ''}" data-cld="${v}"><span class="ke-mi">${ico(ic)}</span><span class="ke-mt"><b>${n}${tag ? `<em>${tag}</em>` : ''}</b><span>${d}</span></span><i class="ke-r"></i></button>`).join('');
  $('#clMarkBox').classList.toggle('hidden', feed); $('#clFeedBox').classList.toggle('hidden', !feed);
  // the Kill-Clipper card on top
  const kn = (KEYS.find(k => k[1] === CL.key) || ['Aus'])[0], off = !CL.key || kn === 'Aus', nameOk = !feed || (CL.name || '').trim().length >= 3;
  $('#kcOn').classList.toggle('on', CL.kills); $('#kcIc').classList.toggle('on', CL.kills && nameOk);
  $('#clStat').classList.toggle('on', CL.kills && nameOk);
  $('#kcTxt').textContent = !CL.kills ? 'Aus. Schalt ihn an, dann wird jeder Kill von selbst gespeichert.'
    : !nameOk ? 'Trag deinen Ingame-Namen ein, so wie er im Kill-Feed steht.'
    : feed ? `An · speichert Kills von „${CL.name.trim()}“ aus dem Kill-Feed${CL.deaths ? ', Tode auch' : ''}`
    : CL.net !== false ? 'An · erkennt Kills am Fadenkreuz. Bis zum ersten erkannten Kill wird jedes Gefecht mitgespeichert.'
    : 'An · erkennt jeden Kill am Fadenkreuz, kein Kill-Feed nötig.';
  $('#kcName').classList.toggle('hidden', !feed);
  $('#kcName').classList.toggle('need', CL.kills && !nameOk);
  $('#clKillsSub').textContent = !CL.kills ? 'Aus' : feed ? 'An · Kill-Feed' : 'An · Fadenkreuz';
  $('#clRecSub').textContent = CL_RECS[CL.recorder][0] + (off ? '' : ' · ' + kn);
  clipStatusUI();
}
const KF_NAMES = { tr: 'oben rechts', tl: 'oben links', r: 'rechts Mitte', tc: 'oben Mitte' };
/** One of the four tiles on top: small label, big value, one short line. Only redrawn when something changed. */
function kcTile(el, k, v, sub, cls = '') {
  if (!el) return;
  const h = `<span class="kc-k">${k}</span><b class="kc-v">${v}</b><em class="kc-s">${sub}</em>`;
  if (el._h !== h) { el._h = h; el.innerHTML = h; }
  el.className = 'kc-st' + (cls ? ' ' + cls : '');
}
const clAgo = d => { const m = (Date.now() - new Date(d)) / 6e4; return m < 2 ? 'gerade eben' : m < 60 ? `vor ${Math.round(m)} Min.` : m < 1440 ? `vor ${Math.round(m / 60)} Std.` : m < 2880 ? 'gestern' : `vor ${Math.round(m / 1440)} Tagen`; };
/** Recording, last clip, kills today, clip key. */
function kcTiles() {
  const s = clSt, own = CL.recorder === 'orbit', rec = CL_RECS[CL.recorder] || CL_RECS.orbit;
  const kn = (KEYS.find(k => k[1] === CL.key) || ['Aus'])[0], off = !CL.key || kn === 'Aus';
  if (!own) kcTile($('#kcRec'), 'Aufnahme', esc(rec[0]), `über ${esc(rec[1])}`);
  else if (!s) kcTile($('#kcRec'), 'Aufnahme', 'Eingebaut', '…');
  else if (s.supported === false) kcTile($('#kcRec'), 'Aufnahme', 'Nicht möglich', 'Dieses Windows kann nicht aufnehmen', 'err');
  else if (s.armed === false) kcTile($('#kcRec'), 'Aufnahme', 'Aus', 'Kill-Clipper und Clip-Taste sind aus', 'off');
  else if (s.running) kcTile($('#kcRec'), 'Aufnahme', '<i class="kc-dot"></i>Läuft', `${Math.min(s.buffered, CL.before + CL.after + 30)} s bereit · ${s.h}p`, 'live');
  else if (s.error) kcTile($('#kcRec'), 'Aufnahme', 'Fehler', esc(s.error), 'err');
  else kcTile($('#kcRec'), 'Aufnahme', s.game ? 'Startet…' : 'Bereit', s.game ? 'Einen Moment' : 'Läuft, sobald FiveM offen ist', 'off');
  const last = clips[0], lt = $('#kcLast');
  if (lt) {
    if (last) { lt.dataset.clip = 0; kcTile(lt, 'Letzter Clip', clAgo(last.date), esc(clLabel(last))); }
    else { delete lt.dataset.clip; kcTile(lt, 'Letzter Clip', clLoaded ? 'Noch keiner' : '…', off ? 'Clips speichert der Kill-Clipper' : `Im Spiel ${esc(kn)} drücken`, 'off'); }
  }
  const today = own ? (HL.n && HL.n.today) || 0 : (s && s.kills) || 0;
  kcTile($('#kcToday'), own ? 'Kills heute' : 'Kills erkannt', today,
    !CL.kills ? 'Kill-Clipper ist aus' : own ? (s && s.kills ? `${s.kills} seit dem Start erkannt` : 'als Clip gespeichert') : 'seit dem Start', CL.kills ? '' : 'off');
  kcTile($('#kcKey'), 'Clip-Taste', off ? 'Aus' : esc(kn), off ? 'Speichern nur per Kill-Clipper' : own ? `${CL.before} s davor · ${CL.after} s danach` : `löst ${esc(rec[1])} aus`, off ? 'warn' : '');
}
function clipStatusUI() {
  if (!$('#kcRec')) return;
  const s = clSt;
  kcTiles();
  const f = $('#clFound'); if (f) f.textContent = CL.area === 'auto' && s && s.found ? `gefunden: ${KF_NAMES[s.found] || s.found}` : '';
  const m = $('#clMarkLast');
  if (m) m.innerHTML = s && s.markAt ? `${s.markImg ? `<img src="${s.markImg}" alt=""${s.markWord ? ' class="strip"' : ''}>` : ''}<span>Zuletzt erkannter Kill: <b>${new Date(s.markAt).toLocaleTimeString('de-DE')}</b> · ${s.markWord ? `Kill-Meldung „${esc(s.markWord)}“` : 'rotes X'}${s.netQuiet ? '<br>Die Erkennung funktioniert hier, das Sicherheitsnetz pausiert.' : ''}</span>`
    : `<span>Seit dem Start noch kein Kill erkannt. Danach siehst du hier, woran OrbitTweak ihn erkannt hat.</span>`;
  // AI check: is a key there, what did it say last
  const ai = s && s.ai, ks = $('#clAiKeyState'), al = $('#clAiLast');
  if (ks) ks.textContent = !ai ? '' : ai.key ? 'gespeichert · zum Ändern neuen Key eintragen' : 'fehlt noch · kostenlos auf openrouter.ai';
  if (al) al.innerHTML = !ai || CL.ai === false ? '' : !ai.key ? '<span>Ohne Key bleibt die KI-Prüfung aus.</span>'
    : `<span>KI-Prüfung ${ai.paused ? 'pausiert (Limit erreicht)' : ai.active ? 'bereit' : 'wartet'} · ${ai.asked || 0} ${ai.asked === 1 ? 'Gefecht' : 'Gefechte'} geprüft, ${ai.kills || 0} ${ai.kills === 1 ? 'Kill' : 'Kills'} gefunden${ai.last ? `<br>Zuletzt: ${esc(ai.last)}` : ''}${ai.error ? `<br>Letzter Fehler: ${esc(ai.error)}` : ''}</span>`;
  const st = $('#clStats');
  if (st) st.textContent = s ? `Seit dem Start: ${s.kills || 0} ${s.kills === 1 ? 'Kill' : 'Kills'} erkannt · ${s.fights || 0} ${s.fights === 1 ? 'Gefecht' : 'Gefechte'} übers Sicherheitsnetz` : '';
}
async function clipStatus() {
  try { clSt = await api('clip.status'); } catch { clSt = null; }
  clipStatusUI();
}
setInterval(() => { if (curPage === 'clip') clipStatus(); }, 2000);
async function clipGameBar() { await api('clip.gamebar'); toast('Hintergrundaufnahme an', 'Hinweis: der Tweak „Xbox Game DVR aus“ ist damit wieder rückgängig'); }
window.clipGameBar = clipGameBar;
async function clipTest() {
  const r = await api('clip.save').catch(e => ({ ok: false, error: e.message }));
  // built-in recorder: the result (file name or reason) arrives as "clip" event
  if (CL.recorder !== 'orbit') toast('Clip gespeichert', `${CL_RECS[CL.recorder][0]} · ${CL_RECS[CL.recorder][1]}`);
  else if (!HOST && !r.ok) toast('Kein Clip gespeichert', r.error || '', false);
  setTimeout(clipList, 4000);
}
window.clipTest = clipTest;
async function clipOpen(rel) { api('clip.open', { rel }); }
window.clipOpen = clipOpen;
let clips = [], clFilter = 'all', clLoaded = false;
// what kind of clip a file of the built-in recorder is, by its name: "Kill …" / "2 Kills …", "Gefecht …", "Highlights …" / "Montage …"
const clKind = c => c.folder !== 'OrbitTweak' ? 'other' : /^(Kill|\d+ Kills) /.test(c.name) ? 'kill' : /^Gefecht /.test(c.name) ? 'fight' : /^(Highlights|Montage) /.test(c.name) ? 'hl' : 'other';
const CL_FILTERS = [['all', 'Alle'], ['kill', 'Kills'], ['fight', 'Gefechte'], ['hl', 'Highlights']];
const CL_NONE = { kill: 'Noch keine Kill-Clips. Sobald der Kill-Clipper einen Kill erkennt, landet er hier.', fight: 'Keine Gefecht-Clips. Die entstehen über das Sicherheitsnetz oder „Nach Gefechten clippen“.', hl: 'Noch keine Highlights. Mach oben aus deinen Kills ein Video.' };
/** Own clips are named "Kill 2026-11-16 21-14-08 (Schnitt)": the word in front is what the clip is. */
const clParse = c => { const m = c.folder === 'OrbitTweak' && /^(.*?) \d{4}-\d\d-\d\d \d\d-\d\d-\d\d(?: \(\d+\))?(.*)$/.exec(c.name); return m ? { kind: m[1], rest: m[2].trim() } : null; };
const clLabel = c => { const p = clParse(c); return p ? p.kind + (p.rest ? ' ' + p.rest : '') : c.name; };
const clWhen = d => {
  const t = new Date(d), n = new Date(), days = Math.round((new Date(n.getFullYear(), n.getMonth(), n.getDate()) - new Date(t.getFullYear(), t.getMonth(), t.getDate())) / 864e5);
  const hm = t.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
  return (days === 0 ? 'Heute' : days === 1 ? 'Gestern' : t.toLocaleDateString('de-DE', { day: 'numeric', month: 'short' })) + ', ' + hm;
};
const clFmt = s => { s = Math.round(s); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; };
// clip length: read from the file itself (only its header), one clip after the other, remembered for the session
const clDur = new Map(); let clDurBusy = false, clDurHold = false, clDurStop = null;
const clDurKey = c => c.rel + '|' + c.size;
function clDurNext() {
  if (clDurBusy || clDurHold || !HOST || curPage !== 'clip') return;
  const c = clips.find(x => x.ext !== '.mkv' && !clDur.has(clDurKey(x))); if (!c) return;
  clDurBusy = true;
  const k = clDurKey(c), v = document.createElement('video');
  // lets go of the file (a file being read can not go to the recycle bin)
  const end = () => { clDurBusy = false; clDurStop = null; clearTimeout(t); v.onloadedmetadata = v.onerror = null; v.removeAttribute('src'); v.load(); };
  const done = d => {
    if (!clDurBusy) return; end();
    if (!isFinite(d)) d = 0;   // a file that is still being written reports no length
    clDur.set(k, d);
    $$('#clGrid .clipc').forEach(cd => { const x = clips[+cd.dataset.clip]; if (x && clDurKey(x) === k && d > 0) { const b = cd.querySelector('.cl-dur'); b.textContent = clFmt(d); b.classList.remove('hidden'); } });
    setTimeout(clDurNext, 40);
  };
  const t = setTimeout(() => done(0), 5000);
  v.preload = 'metadata'; v.muted = true;
  v.onloadedmetadata = () => done(v.duration); v.onerror = () => done(0);
  clDurStop = end;
  v.src = 'https://clips.orbit/' + c.rel.split('/').map(encodeURIComponent).join('/');
}
/** No reading of clip files while clips are moved to the recycle bin or deleted. */
function clDurPause(on) { clDurHold = on; if (on) clDurStop?.(); else clDurNext(); }
function clipRender() {
  const n = k => clips.filter(c => k === 'all' || clKind(c) === k).length;
  $('#clFilter').innerHTML = CL_FILTERS.map(([k, t]) => `<button class="${clFilter === k ? 'on' : ''}" data-clf="${k}">${t}<i class="cl-cnt">${n(k)}</i></button>`).join('');
  const shown = clips.map((c, i) => [c, i]).filter(([c]) => clFilter === 'all' || clKind(c) === clFilter);
  // one button clears away all firefight clips at once (they go to the recycle bin)
  const tb = $('#clTrash'), fights = n('fight');
  tb.classList.toggle('hidden', clFilter !== 'fight' || !fights); tb.querySelector('span').textContent = `Alle ${fights} in den Papierkorb`;
  const kn = (KEYS.find(k => k[1] === CL.key) || ['Aus'])[0];
  $('#clGrid').innerHTML = shown.length ? shown.map(([c, i]) => {
    const p = clParse(c), k = clKind(c), d = c.dur || clDur.get(clDurKey(c)) || 0;
    const bd = p ? `<span class="cl-bd ${k}">${ico(k === 'kill' ? 'crosshair' : k === 'fight' ? 'target' : k === 'hl' ? 'sparkle' : 'rec')}${esc(p.kind)}</span>` : `<span class="cl-bd">${esc(c.folder || 'Videos')}</span>`;
    const acts = (c.ext === '.mp4' ? `<button class="cl-act" data-clshare="${i}" title="Für Discord in die Zwischenablage (über ${CL.shareMb || 10} MB wird der Clip vorher verkleinert)">${ico('copy')}</button>` : '')
      + (c.folder === 'OrbitTweak' ? `<button class="cl-act d" data-cldel="${i}" title="In den Papierkorb">${ico('trash')}</button>` : '');
    return `<div class="clipc" data-clip="${i}"><div class="v"><img alt="" data-cth="${i}"><div class="noprev">${ico('film')}</div><div class="playi">${ico('play')}</div>${bd}<span class="cl-dur${d ? '' : ' hidden'}">${d ? clFmt(d) : ''}</span></div>`
      + `<div class="m"><div class="m-t"><b>${p ? clWhen(c.date) : esc(c.name)}</b><span>${p ? (p.rest ? esc(p.rest.replace(/[()]/g, '')) + ' · ' : '') + bytes(c.size) : clWhen(c.date) + ' · ' + bytes(c.size)}</span></div>${acts}</div></div>`;
  }).join('') : clips.length ? `<div class="cl-empty sm"><p>${CL_NONE[clFilter] || 'Hier gibt es noch keine Clips.'}</p></div>`
    : `<div class="cl-empty"><div class="cl-ghost" aria-hidden="true"><i></i><i>${ico('play')}</i><i></i></div><b>${clLoaded ? 'Noch keine Clips' : 'Clips werden geladen…'}</b>`
      + (clLoaded ? `<p>${CL.kills ? 'Der Kill-Clipper ist an, dein nächster Kill landet hier.' : 'Schalt den Kill-Clipper an, dann wird jeder Kill von selbst gespeichert.'}${!CL.key || kn === 'Aus' ? '' : ` Oder drück im Spiel <b>${esc(kn)}</b>.`}</p>${CL.kills ? '' : `<button class="btn p" data-clkon>${ico('crosshair')}Kill-Clipper anschalten</button>`}` : '') + '</div>';
  $$('#clGrid img[data-cth]').forEach(img => thumb('clip.thumb', { rel: clips[+img.dataset.cth].rel }, img));
  clDurNext();
}
async function clipList() {
  try { clips = await api('clip.list'); } catch { clips = []; }
  clLoaded = true;
  $('#clCount').textContent = clips.length;
  clipRender(); kcTiles();
}
window.clipList = clipList;
async function clipTrash(list, what) {
  if (!list.length || !await confirmBox(`${what} in den Papierkorb?`, 'Von dort kannst du sie wiederherstellen, solange der Papierkorb nicht geleert wird.')) return;
  clDurPause(true);
  const r = await api('clip.recycle', { rels: list.map(c => c.rel) }).catch(e => ({ deleted: 0, failed: list.length, error: e.message }));
  clDurPause(false);
  if (r.deleted) toast(r.deleted === 1 ? 'Clip im Papierkorb' : `${r.deleted} Clips im Papierkorb`, r.failed ? `${r.failed} ließen sich nicht verschieben` : '');
  else toast('Nichts verschoben', r.error || 'Die Dateien ließen sich nicht in den Papierkorb legen', false);
  clipList(); hlLoad();
}
/** "Share for Discord": puts the clip on the clipboard as a file; a clip over the limit is made smaller first. */
let clSharing = false;
async function clipShare(c) {
  if (clSharing) return toast('Moment', 'Es wird gerade schon ein Clip vorbereitet', false);
  clSharing = true;
  const mb = CL.shareMb || 10, big = c.size > mb * 1048576;
  if (big) { toast('Clip wird verkleinert', `${bytes(c.size)} sind mehr als ${mb} MB, das dauert ein paar Sekunden`); hlProgress({ pct: 1, text: 'Clip wird verkleinert…' }); }
  const r = await api('clip.share', { rel: c.rel }).catch(e => ({ error: e.message }));
  clSharing = false; $('#hlProg').classList.add('hidden');
  if (r.error) return toast('Clip nicht bereit', r.error, false);
  if (r.clipboard) passed('In der Zwischenablage', `${bytes(r.size)}${r.shrunk ? ' · verkleinert' : ''} · in Discord mit Strg+V einfügen`);
  else toast('Zwischenablage belegt', `Die Datei liegt bereit: ${r.name}`, false);
}
window.clipShareRel = rel => { const c = clips.find(x => x.rel === rel); if (c) clipShare(c); };
document.addEventListener('click', e => {
  const sh = e.target.closest('[data-clshare]'); if (sh) { e.stopPropagation(); clipShare(clips[+sh.dataset.clshare]); return; }
  const f = e.target.closest('[data-clf]'); if (f) { clFilter = f.dataset.clf; clipRender(); return; }
  if (e.target.closest('#clTrash')) { const l = clips.filter(c => clKind(c) === 'fight'); clipTrash(l, `Alle ${l.length} Gefecht-Clips`); return; }
  const d = e.target.closest('[data-cldel]'); if (d) { e.stopPropagation(); const c = clips[+d.dataset.cldel]; clipTrash([c], `„${c.name}“`); }
}, true);

/* ======================= HIGHLIGHT VIDEO (kill clips joined into one) ======================= */
const HL = { scope: 'today', tail: 0, busy: false, n: { today: 0, all: 0 }, mode: 'mo' };
try { HL.mode = localStorage.getItem('orbitHlMode') === 'hl' ? 'hl' : 'mo'; } catch { }
function hlUI() {
  if (!$('#hlGo')) return;
  // "Montage mit Effekten" or "Einfach aneinander": one card, one big button
  const mo = HL.mode !== 'hl';
  $$('#hlMode button').forEach(b => b.classList.toggle('on', b.dataset.v === (mo ? 'mo' : 'hl')));
  ['#moOpts', '#moInfo', '#moGo'].forEach(id => $(id).classList.toggle('hidden', !mo));
  ['#hlOpts', '#hlInfo', '#hlGo'].forEach(id => $(id).classList.toggle('hidden', mo));
  $$('#hlScope button').forEach(b => b.classList.toggle('on', b.dataset.v === HL.scope));
  $$('#hlTail button').forEach(b => b.classList.toggle('on', +b.dataset.v === HL.tail));
  const n = HL.n[HL.scope] || 0;
  $('#hlInfo').innerHTML = n >= 2 ? `<b>${n} Kill-Clips${HL.scope === 'today' ? ' von heute' : ''}</b> werden aneinandergehängt${HL.tail ? ', von jedem die letzten ' + HL.tail + ' Sekunden' : ''}. Ohne Effekte, geht schnell.`
    : `Dafür braucht es mindestens zwei Kill-Clips${HL.scope === 'today' ? ' von heute' : ''}, bisher ${n === 1 ? 'ist es einer' : 'gibt es keinen'}.`;
  const b = $('#hlGo'); b.disabled = HL.busy || n < 2; b.innerHTML = HL.busy ? `${ico('refresh', 'i spin')}<span>Wird erstellt…</span>` : `${ico('film')}<span>Video erstellen</span>`;
  $('#hlCard').classList.toggle('busy', HL.busy);
  kcTiles();
}
async function hlLoad() { try { const r = await api('clip.kills'); HL.n = { today: r.today, all: r.all }; HL.busy = !!r.busy; } catch { } hlUI(); }
function hlProgress(d) { const p = $('#hlProg'); if (!p) return; p.classList.remove('hidden'); $('#hlBar').style.width = d.pct + '%'; $('#hlTxt').textContent = d.text; }
async function hlMake() {
  if (HL.busy) return;
  HL.busy = true; hlUI(); hlProgress({ pct: 1, text: 'Starte…' });
  const r = await api('clip.highlight', { scope: HL.scope, tail: HL.tail }).catch(e => ({ error: e.message }));
  HL.busy = false; $('#hlProg').classList.add('hidden');
  if (r.error) toast('Kein Highlight-Video', r.error, false);
  else passed('Highlight-Video fertig', `${r.clips} Clips · ${r.file}`);
  clipList(); hlLoad();
}
document.addEventListener('click', e => {
  const md = e.target.closest('#hlMode button'); if (md) { HL.mode = md.dataset.v; try { localStorage.setItem('orbitHlMode', HL.mode); } catch { } hlUI(); return; }
  const s = e.target.closest('#hlScope button'); if (s) { HL.scope = s.dataset.v; hlUI(); return; }
  const t = e.target.closest('#hlTail button'); if (t) { HL.tail = +t.dataset.v; CL.highlightTail = HL.tail; hlUI(); clipPush(); return; }   // also counts for the automatic video
  if (e.target.closest('#hlGo')) hlMake();
});

document.addEventListener('click', e => {
  const r = e.target.closest('[data-rec]'); if (r) { CL.recorder = r.dataset.rec; clipUI(); clipPush(); return; }
  const k = e.target.closest('[data-clk]'); if (k) { CL.key = +k.dataset.clk; clipUI(); clipPush(); return; }
  const q = e.target.closest('[data-clq]'); if (q) { CL.quality = q.dataset.clq; clipUI(); clipPush(); return; }
  const cb = e.target.closest('[data-clb]'); if (cb) { CL.before = +cb.dataset.clb; clipUI(); clipPush(); return; }
  const ca = e.target.closest('[data-cla]'); if (ca) { CL.after = +ca.dataset.cla; clipUI(); clipPush(); return; }
  if (e.target.closest('#clSound')) { CL.sound = !CL.sound; clipUI(); clipPush(); return; }
  if (e.target.closest('#clFlash')) { CL.flash = !CL.flash; clipUI(); clipPush(); return; }
  if (e.target.closest('#clAuto')) { CL.auto = !CL.auto; clipUI(); clipPush(); toast(CL.auto ? 'Auto-Clip an' : 'Auto-Clip aus', CL.auto ? 'Nach jedem Gefecht wird automatisch ein Clip gespeichert' : ''); return; }
  const cs = e.target.closest('[data-cls]'); if (cs) { CL.sens = +cs.dataset.cls; clipUI(); clipPush(); return; }
  const cd = e.target.closest('[data-cld]'); if (cd) { CL.detect = cd.dataset.cld; clipUI(); clipPush(); return; }
  const hx = e.target.closest('#clHudX button');
  if (hx) { if (hx.dataset.hx === 'hudTime') CL.hudTime = CL.hudTime === false; else CL.hudClock = !CL.hudClock; clipUI(); clipPush(); return; }
  if (e.target.closest('#clHud')) { CL.hud = CL.hud === false; clipUI(); clipPush(); toast(CL.hud ? 'Zähler im Spiel an' : 'Zähler im Spiel aus', CL.hud ? 'Oben links: Aufnahme-Punkt und Kills der Runde' : ''); return; }
  if (e.target.closest('#clAi')) { CL.ai = CL.ai === false; clipUI(); clipPush(); toast(CL.ai ? 'KI-Prüfung an' : 'KI-Prüfung aus', CL.ai ? 'Gefechte ohne erkannten Kill prüft ein KI-Modell' : 'Es entscheidet nur noch die Erkennung am Fadenkreuz'); return; }
  if (e.target.closest('#clNet')) { CL.net = CL.net === false; clipUI(); clipPush(); toast(CL.net ? 'Sicherheitsnetz an' : 'Sicherheitsnetz aus', CL.net ? 'Bis der erste Kill erkannt ist, werden Gefechte trotzdem gespeichert' : 'Es werden nur noch erkannte Kills gespeichert'); return; }
  const top = e.target.closest('#kcOn, [data-clkon]');
  if (top || e.target.closest('#clKills')) {
    CL.kills = !CL.kills; clipUI(); clipPush(); if (!clips.length) clipRender();   // only the empty grid talks about the Kill-Clipper
    if (CL.kills && CL.detect === 'feed' && (CL.name || '').trim().length < 3) { toast('Ingame-Namen eintragen', 'Ohne Namen weiß OrbitTweak nicht, welche Kills deine sind', false); $(top ? '#kcName' : '#clName').focus(); }
    else toast(CL.kills ? 'Kill-Clipper an' : 'Kill-Clipper aus', CL.kills ? 'Ab jetzt wird jeder Kill automatisch gespeichert' : '');
    return;
  }
  if (e.target.closest('#clDeaths')) { CL.deaths = !CL.deaths; clipUI(); clipPush(); return; }
  if (e.target.closest('#clBanner')) { CL.banner = CL.banner === false; clipUI(); clipPush(); toast(CL.banner ? 'Kill-Einblendung an' : 'Kill-Einblendung aus', CL.banner ? 'Bei jedem erkannten Kill erscheint kurz dein Kill-Zähler · Server-Regeln beachten' : ''); return; }
  if (e.target.closest('#hlAuto')) { CL.autoHighlight = !CL.autoHighlight; clipUI(); clipPush(); toast(CL.autoHighlight ? 'Automatisches Highlight-Video an' : 'Automatisches Highlight-Video aus', CL.autoHighlight ? 'Wird erstellt, sobald du FiveM schließt und mindestens zwei Kills geclippt wurden' : ''); return; }
  const sm = e.target.closest('[data-shmb]'); if (sm) { CL.shareMb = +sm.dataset.shmb; clipUI(); clipPush(); return; }
  const ka = e.target.closest('[data-kfa]'); if (ka) { CL.area = ka.dataset.kfa; clipUI(); clipPush(); return; }
  if (e.target.closest('#clKfTest')) { kfTest(); return; }
  if (e.target.closest('#clMkTest')) { mkTest(); return; }
  const c = e.target.closest('[data-clip]'); if (c) {
    const cl = clips[+c.dataset.clip], src = 'https://clips.orbit/' + cl.rel.split('/').map(encodeURIComponent).join('/');
    // title: what the clip is and when; the preview picture stands in until the video has loaded
    const p = clParse(cl), k = clKind(cl), d = cl.dur || clDur.get(clDurKey(cl)) || 0, poster = c.querySelector('img[data-cth]')?.getAttribute('src') || '';
    const name = p ? p.kind + (p.rest ? ' ' + p.rest.replace(/[()]/g, '') : '') : cl.name;
    const sub = [clWhen(cl.date), d ? clFmt(d) : '', bytes(cl.size), p ? '' : esc(cl.folder || '')].filter(Boolean).join(' · ');
    modal(`<div class="cpl"><div class="cpl-h"><span class="cpl-ic">${ico(k === 'kill' ? 'crosshair' : k === 'fight' ? 'target' : k === 'hl' ? 'sparkle' : 'film')}</span><div class="cpl-t"><b>${esc(name)}</b><span>${sub}</span></div><button class="ib cpl-x" onclick="closeModal()" title="Schließen (Esc)">${ico('x')}</button></div>
      <div class="player">${cl.ext === '.mkv' ? `<div class="cpl-none"${poster ? ` style="background-image:linear-gradient(rgba(8,10,16,.8),rgba(8,10,16,.88)),url('${esc(poster)}')"` : ''}>${ico('videooff')}<b>MKV kann hier nicht abgespielt werden</b><span>Zeig den Clip im Ordner und öffne ihn mit deinem Player.</span></div>` : `<video src="${src}"${poster ? ` poster="${esc(poster)}"` : ''} controls autoplay></video>`}</div>
      <div class="act"><button class="btn${cl.ext === '.mkv' ? ' p' : ''}" onclick="clipOpen('${esc(cl.rel)}')">${ico('folder')}Im Ordner zeigen</button>${cl.ext === '.mp4' ? `<button class="btn" onclick="clipShareRel('${esc(cl.rel)}')">${ico('copy')}Für Discord kopieren</button>` : ''}<span class="sp"></span><button class="btn gh d" onclick="clipDel('${esc(cl.rel)}')">${ico('trash')}Löschen</button>${cl.ext === '.mp4' ? `<button class="btn p" onclick="cutOpen('${esc(cl.rel)}')">${ico('film')}Schneiden</button>` : ''}</div></div>`);
    $('#mbox').style.width = '880px';
  }
});
async function clipDel(rel) {
  // the clip still playing in the window holds its file open, and Windows deletes no file in use: stop it and let go first
  const v = $('#mbox video');
  if (v) { try { v.pause(); v.removeAttribute('src'); v.load(); } catch { } await sleep(400); }
  clDurPause(true); const r = await api('clip.delete', { rel }).catch(e => ({ ok: false, error: e.message })); clDurPause(false); closeModal(); $('#mbox').style.width = ''; toast(r.ok ? 'Clip gelöscht' : 'Löschen fehlgeschlagen', r.error || '', r.ok); clipList(); }
window.clipDel = clipDel;
function initClip(s) { Object.assign(CL, s.clip || {}); MO.music = CL.montageMusic ? CL.montageMusic.split(/[\\/]/).pop() : null; clipUI(); clipPush(); }

/* ======================= MODS ======================= */
const MOD_CATS = { minimap: ['Minimap', 'map', 'Eigene Karten & Farben'], kill: ['Kill-Effekte', 'crosshair', 'Effekte bei Treffern & Kills'], gfx: ['Grafik-Packs', 'sun', 'Licht, Wetter, Farben'], sound: ['Sounds', 'activity', 'Waffen & Fahrzeuge'], skin: ['Waffen-Skins', 'crosshair', 'Skins & Texturen für Waffen'], other: ['Sonstiges', 'puzzle', 'Alles andere'] };
function modCatsUI_unused() { $('#modCats').innerHTML = Object.entries(MOD_CATS).map(([k, [n, i, d]]) => `<div class="card hov mcat" onclick="modInstall('${k}')"><div class="qi">${ico(i)}</div><b>${n}</b><span>${d}</span><div class="sub" style="font-size:11.5px;margin-top:8px;color:var(--a)">+ Installieren</div></div>`).join(''); }
async function modInstall(cat, ok, reshade) {
  if (!S.settings.modsAck && !ok) return modal(`<div class="kicker">Hinweis</div><h1 style="margin-top:10px">Mods auf eigene <span class="grad">Verantwortung</span></h1>
    <p>Viele FiveM-Server verbieten Mods (Pure-Mode). Mit Mods kannst du dort nicht joinen oder wirst gekickt, manche Server bannen dafür. Installiere nur Mods aus vertrauenswürdigen Quellen.</p>
    <div class="act"><button class="btn" onclick="closeModal()">Abbrechen</button><button class="btn p lg" onclick="modAck('${cat}',${reshade ? 'true' : 'false'})">${ico('check')}Verstanden</button></div>`);
  const r = await modRun('mods.install', { cat });
  if (r.cancelled) return;
  if (r.error) toast('Nicht installiert', r.error, false); else modReport(r);
  modList();
}
// an archive with several versions (FiveM / Singleplayer …): ask which one, then the same call again with the choice
async function modRun(cmd, args) {
  const r = await api(cmd, args).catch(e => ({ error: e.message }));
  if (!r || !r.choose || !r.choose.length) return r || {};
  const v = await modPick(r.choose);
  return v == null ? { cancelled: true } : modRun(cmd, { ...args, files: r.files || args.files, variant: v });
}
function modPick(list) {
  return new Promise(res => {
    modal(`<div class="kicker">Mod installieren</div><h1 style="margin-top:10px">Mehrere <span class="grad">Versionen</span></h1><p>Mehrere Versionen im Archiv – welche installieren?</p>
      <div class="mvar">${list.map((v, i) => `<button class="btn" data-mvar="${i}">${ico('folder')}<span>${esc(v || 'Hauptordner')}</span>${/fivem/i.test(v) ? '<em class="chip a">FiveM</em>' : ''}</button>`).join('')}</div>
      <div class="act"><button class="btn" id="mvNo">Abbrechen</button></div>`);
    $$('#mbox [data-mvar]').forEach(b => b.onclick = () => { closeModal(); res(list[+b.dataset.mvar]); });
    $('#mvNo').onclick = () => { closeModal(); res(null); };
  });
}
// after an install: what was detected and where it went (sound pack → citizen folder, raw archive wrapped …)
const MOD_FOOT = 'Aktiv beim nächsten FiveM-Start. Ab zwei Paketen steht unten links „N mod packs loaded“. Server mit Pure-Mode ignorieren Mods aus dem FiveM-Ordner.';
function modReport(r) {
  const notes = [...new Set(r.notes || [])].concat((r.off || []).map(t => `„${t}“ wurde ausgeschaltet, weil es dieselben Dateien ersetzt.`));
  const names = (r.mods || []).map(m => m.name).filter(Boolean);
  if (!notes.length) return passed('Mod installiert', `${names.join(', ') || MOD_CATS[(r.mods || [])[0]?.cat]?.[0] || 'Mod'} · aktiv beim nächsten FiveM-Start`);
  modal(`<div class="kicker">Mod installiert</div><h1 style="margin-top:10px">${esc(names.join(', ') || 'Fertig')}</h1>
    <ul class="mnotes">${notes.map(n => `<li>${ico(/^(Sound-Pack erkannt|Rohes Archiv)/.test(n) ? 'check' : 'alert')}<span>${esc(n)}</span></li>`).join('')}</ul>
    <p class="mfoot">${MOD_FOOT}</p><div class="act"><button class="btn p" onclick="closeModal()">${ico('check')}Alles klar</button></div>`);
}
function modAck(cat, reshade) { closeModal(); S.settings.modsAck = true; api('settings.set', { key: 'modsAck', value: true }); if (reshade) { const m = LIB.items.find(x => x.type === 'reshade' && x.preset === (RS.preset || 'vivid')) || LIB.items.find(x => x.type === 'reshade'); if (m) libInstall(m.id); } else if (cat !== '__dl' && cat !== '__browse') modInstall(cat, true); }
window.modInstall = modInstall; window.modAck = modAck;
const MOD_WHERE = { mods: 'mods-Ordner', citizen: 'citizen-Ordner', gta: 'GTA-V-Ordner' };
async function modList() {
  let r; try { r = await api('mods.list'); } catch { r = { found: false, items: [] }; }
  const items = r.items || [], lp = +(S.settings.launch?.pure || 0);
  $('#modCount').textContent = items.length; if ($('#mtFiles')) $('#mtFiles').textContent = items.length + ' installiert';
  if ($('#modGta')) { $('#modGta').classList.toggle('on', !!S.settings.modSoundGta); $('#modGtaHint').textContent = r.found && r.gta === false ? 'GTA-V-Ordner nicht gefunden (Pfad in CitizenFX.ini). Sounds landen dann im citizen-Ordner.' : ''; }
  const row = m => {
    const where = (m.places || ['mods']).map(p => MOD_WHERE[p] || p).join(' + ');
    // a mod in FiveM's own folders only loads without pure mode; sounds in the GTA folder also on pure 1
    const warn = m.broken ? `<div class="mwarn">${ico('alert')}<span>${esc(m.broken)}</span>${m.fix ? `<button class="btn sm" data-modfix="${esc(m.id)}">${ico('refresh')}Reparieren</button>` : ''}</div>`
      : m.on && lp > (m.pure || 0) ? `<div class="mwarn">${ico('alert')}<span>Dein Launcher startet FiveM mit Pure ${lp} – dieser Mod wird dann nicht geladen.</span></div>` : '';
    return `<div class="mod ${m.broken ? 'bad' : ''} ${m.on ? 'on' : ''}"><div class="ti mico">${ico(m.broken ? 'alert' : MOD_CATS[m.cat]?.[1] || 'puzzle')}</div><div class="tx"><b>${esc(m.name)}</b><p>${MOD_CATS[m.cat]?.[0] || 'Sonstiges'} · ${bytes(m.size)} · <span class="mwhere" title="${esc((m.paths || []).join('\n'))}">${where}</span> · ${m.pure ? 'auch auf Pure 1' : 'nur ohne Pure-Mode'}</p>${warn}</div><span class="chip ${m.on ? 'g' : ''}">${m.on ? 'Aktiv' : 'Aus'}</span><div class="tg ${m.on ? 'on' : ''}" data-mod="${esc(m.id)}" data-title="${esc(m.name)}"></div><button class="ib" title="Löschen" data-moddel="${esc(m.id)}" data-title="${esc(m.name)}" data-orig="${(m.places || []).some(p => p !== 'mods') ? 1 : ''}">${ico('trash')}</button></div>`;
  };
  $('#modList').innerHTML = !r.found ? emptyBox('alert', 'FiveM nicht gefunden', 'Wähl den FiveM-Ordner im Launcher, dann erscheinen deine Mods hier.', `<button class="btn" onclick="go('launcher')">${ico('rocket')}Zum Launcher</button>`)
    : items.length ? items.map(row).join('') : emptyBox('puzzle', 'Noch keine Mods installiert', 'Lade oben ein Pack herunter oder nimm „Datei installieren“.');
  const mm = items.filter(m => m.cat === 'minimap');
  if ($('#mmMods')) $('#mmMods').innerHTML = mm.map(row).join('');
}
document.addEventListener('click', async e => {
  const t = e.target.closest('[data-mod]');
  if (t) {
    const on = !t.classList.contains('on'), r = await api('mods.toggle', { id: t.dataset.mod, title: t.dataset.title, on });
    if (r.error) toast('Geht gerade nicht', r.error, false);
    else toast(on ? 'Mod aktiviert' : 'Mod deaktiviert', (r.off || []).length ? `„${r.off.join('“, „')}“ ausgeschaltet – ersetzt dieselben Dateien` : t.dataset.title);
    modList(); return;
  }
  const d = e.target.closest('[data-moddel]');
  if (d && await confirmBox('Mod löschen?', d.dataset.title + ' wird entfernt.' + (d.dataset.orig ? ' Ersetzte Originaldateien kommen zurück.' : ''), { ok: 'Löschen', danger: true })) { const r = await api('mods.delete', { id: d.dataset.moddel, title: d.dataset.title }); if (r.error) toast('Nicht gelöscht', r.error, false); modList(); return; }
  const f = e.target.closest('[data-modfix]');
  if (f) {
    f.disabled = true; f.innerHTML = `${ico('refresh', 'i spin')}Repariere…`;
    const r = await api('mods.repair', { id: f.dataset.modfix }).catch(er => ({ error: er.message }));
    if (r.error) toast('Nicht repariert', r.error, false); else modReport(r);
    modList(); return;
  }
  if (e.target.closest('#modGta')) {
    S.settings.modSoundGta = !S.settings.modSoundGta; $('#modGta').classList.toggle('on', S.settings.modSoundGta);
    api('settings.set', { key: 'modSoundGta', value: S.settings.modSoundGta });
    toast(S.settings.modSoundGta ? 'Sounds in den GTA-V-Ordner' : 'Sounds in den citizen-Ordner', 'Gilt für neu installierte Sound-Packs');
  }
});

/* ======================= SNAP TAP / CONTROLLER / NET ======================= */
document.addEventListener('click', e => {
  if (!e.target.closest('#snapTg')) return;
  S.settings.snapTap = !S.settings.snapTap; $('#snapTg').classList.toggle('on', S.settings.snapTap); wsGame();
  api('settings.set', { key: 'snapTap', value: S.settings.snapTap });
  toast(S.settings.snapTap ? 'Snap Tap an' : 'Snap Tap aus', S.settings.snapTap ? 'Nur aktiv, solange FiveM vorne ist · Server-Regeln beachten' : '');
});
/* ======================= WASD TWEAK ("Laggen", fast key repeat, keyboard buffer) ======================= */
// Laggen: held movement keys are let go for "gap" ms every "every" ms while the game is in front (stop-and-go).
// Fast key repeat: Windows keyboard filter, takes effect at once.
// Buffer: 0 = Windows default (100), a smaller buffer needs a restart of Windows.
const WASD_LEVELS = [[0, 'Standard'], [50, '50'], [30, '30'], [25, '25'], [20, '20'], [16, '16']];
const WASD_TXT = { 0: 'Windows-Standard (100)', 50: 'leicht verkleinert', 30: 'mittel', 25: 'klein', 20: 'sehr klein', 16: 'kleinste Stufe, dabei können Tasten verschluckt werden' };
const CHOP_PRE = { 1: [25, 130], 2: [35, 80], 3: [45, 60], 4: [60, 75] };   // let-go ms, ms from one let-go to the next
let wasdSt = null, chopT = 0;
function chopPush(wait = 250) {
  clearTimeout(chopT);
  chopT = setTimeout(async () => {
    const c = wasdSt.chop; const r = await api('wasd.chop', { on: c.on, gap: c.gap, every: c.every, key: c.key || 0 }).catch(() => null);
    if (r && r.chop) { wasdSt = r; wasdUI(); if (r.chop.key && r.chop.keyOk === false) toast('Taste belegt', 'Andere Taste für das Laggen wählen', false); }
  }, wait);
}
/** The small note shown at the top of the game picture when the key switches "Laggen" on or off. */
function chopFlashPng(on) {
  const cv = document.createElement('canvas'); cv.width = 300; cv.height = 52; const x = cv.getContext('2d');
  x.fillStyle = 'rgba(10,10,12,.9)'; x.beginPath(); x.roundRect(0, 0, 300, 52, 12); x.fill();
  const a = getComputedStyle(document.documentElement).getPropertyValue('--a').trim();
  x.fillStyle = on ? a : '#52525B'; x.fillRect(0, 9, 4, 34);
  x.fillStyle = on ? '#3DFF8B' : '#71717A'; x.beginPath(); x.arc(28, 26, 7, 0, Math.PI * 2); x.fill();
  x.fillStyle = '#fff'; x.font = '700 17px Geist, Inter, sans-serif'; x.fillText(on ? 'LAGGEN AN' : 'LAGGEN AUS', 48, 25);
  x.fillStyle = '#A1A1AA'; x.font = '500 12px Inter, sans-serif'; x.fillText('OrbitTweak · WASD-Tweak', 48, 41);
  return cv.toDataURL('image/png').split(',')[1];
}
function wasdUI() {
  const el = $('#wasdSeg'); if (!el) return;
  const s = wasdSt || (wasdSt = { size: 0, pending: false, lag: false }); s.chop ||= { on: false, gap: 30, every: 90 };
  const c = s.chop, rng = (id, v, u) => { const r = $(id); r.value = v; r.style.setProperty('--p', (v - r.min) / (r.max - r.min) * 100 + '%'); $(id + 'V').textContent = v + u; };
  $('#chopTg').classList.toggle('on', !!c.on);
  $$('#chopPre button').forEach(b => { const p = CHOP_PRE[b.dataset.v]; b.classList.toggle('on', p[0] === c.gap && p[1] === c.every); });
  rng('#chopGap', c.gap, ' ms'); rng('#chopEvery', c.every, ' ms');
  $('#chopNote').innerHTML = `<b>${c.on ? 'An' : 'Aus'}</b>: alle ${c.every} ms zuckt die Figur für ${c.gap} ms zur Seite, abwechselnd links und rechts, und läuft dabei weiter. Stärker zucken = längerer Zucker, öfter zucken = kürzerer Abstand.`;
  $('#chopKey').innerHTML = KEYS.map(([n, v]) => `<button class="${(c.key || 0) === v ? 'on' : ''}" data-chopk="${v}">${n}</button>`).join('');
  const kn = (KEYS.find(k => k[1] === (c.key || 0)) || ['Aus'])[0];
  $('#chopKeyNote').textContent = c.key ? (c.keyOk === false ? `${kn} ist schon von etwas anderem belegt, nimm eine andere Taste.` : `${kn} schaltet das Laggen im Spiel an und aus, oben im Bild erscheint kurz „LAGGEN AN“ oder „LAGGEN AUS“.`) : 'Keine Taste, nur hier umschaltbar.';
  $('#lagTg').classList.toggle('on', !!s.lag);
  $('#lagNote').innerHTML = s.lag ? `<b>An</b>: Tasten wiederholen sich nach ${s.delay} ms alle ${s.repeat} ms.` : '<b>Aus</b>: normale Tastenwiederholung von Windows.';
  el.innerHTML = WASD_LEVELS.map(([v, n]) => `<button class="${s.size === v ? 'on' : ''}" data-wasd="${v}">${n}</button>`).join('');
  $('#wasdNote').innerHTML = `<b>${s.size ? 'Puffer ' + s.size : 'Standard'}</b>: ${WASD_TXT[s.size] || 'eigener Wert'}.`
    + (s.pending ? ' <span class="wasd-pend">Gespeichert. Wirkt erst, wenn du Windows über „Neu starten“ neu startest („Herunterfahren“ reicht nicht).</span>' : '');
}
async function wasdLoad() {
  try { wasdSt = await api('wasd.get'); } catch { wasdSt = null; }
  wasdUI();
  api('wasd.flash', { on: chopFlashPng(true), off: chopFlashPng(false) }).catch(() => { });
}
document.addEventListener('click', async e => {
  if (e.target.closest('#chopTg')) {
    wasdSt.chop.on = !wasdSt.chop.on; wasdUI(); chopPush(0);
    toast(wasdSt.chop.on ? 'WASD-Laggen an' : 'WASD-Laggen aus', wasdSt.chop.on ? 'Nur aktiv, solange FiveM vorne ist · Server-Regeln beachten' : '');
    return;
  }
  const pre = e.target.closest('#chopPre button');
  if (pre) { const p = CHOP_PRE[pre.dataset.v]; wasdSt.chop.gap = p[0]; wasdSt.chop.every = p[1]; wasdUI(); chopPush(0); return; }
  const ck = e.target.closest('[data-chopk]');
  if (ck) { wasdSt.chop.key = +ck.dataset.chopk; wasdSt.chop.keyOk = true; wasdUI(); chopPush(0); return; }
  if (e.target.closest('#lagTg')) {
    const on = !(wasdSt && wasdSt.lag);
    const r = await api('wasd.lag', { on }).catch(err => ({ error: err.message }));
    if (!r || r.error) { if (r && r.size !== undefined) { wasdSt = r; wasdUI(); } return toast('Tastenwiederholung nicht umgestellt', (r && r.error) || '', false); }
    wasdSt = r; wasdUI();
    toast(r.lag ? 'Schnelle Tastenwiederholung an' : 'Schnelle Tastenwiederholung aus', r.lag ? 'Wirkt sofort, auch beim Tippen' : 'Normale Tastenwiederholung');
    return;
  }
  const b = e.target.closest('[data-wasd]'); if (!b) return;
  const r = await api('wasd.set', { size: +b.dataset.wasd }).catch(err => ({ error: err.message }));
  if (!r || r.error) return toast('Tastatur-Puffer nicht gesetzt', (r && r.error) || '', false);
  wasdSt = r; wasdUI(); loadTweaks().catch(() => { });
  toast(r.size ? `Tastatur-Puffer ${r.size}` : 'Tastatur-Puffer: Standard', 'Wirkt nach „Neu starten“ von Windows');
});
document.addEventListener('input', e => {
  if (e.target.id !== 'chopGap' && e.target.id !== 'chopEvery') return;
  const c = wasdSt.chop;
  if (e.target.id === 'chopGap') c.gap = +e.target.value; else c.every = +e.target.value;
  if (c.every < c.gap + 15) c.every = Math.ceil((c.gap + 15) / 5) * 5;   // a key has to be down for a moment between two let-goes
  wasdUI(); chopPush();
});
const GP_NAMES = ['A', 'B', 'X', 'Y', 'LB', 'RB', 'LT', 'RT', 'Back', 'Start', 'LS', 'RS', '↑', '↓', '←', '→', 'Guide'];
let gpRaf = 0;
function gpStart() {
  cancelAnimationFrame(gpRaf);
  $('#gpBtns').innerHTML = GP_NAMES.map((n, i) => `<i data-gb="${i}">${n}</i>`).join('');
  $('#gpLT').style.transition = $('#gpRT').style.transition = 'none';
  $('#gpLT').style.transform = $('#gpRT').style.transform = 'scaleX(0)';
  const loop = () => {
    if (curPage !== 'input' || $('#padPad').classList.contains('hidden')) return;
    const gp = [...(navigator.getGamepads ? navigator.getGamepads() : [])].find(Boolean);
    $('#gpNone').classList.toggle('hidden', !!gp); $('#gpLive').classList.toggle('hidden', !gp);
    if (gp) {
      const ax = gp.axes, mv = (el, x, y) => el.style.transform = `translate(${(x || 0) * 40}px,${(y || 0) * 40}px)`;
      mv($('#gpL'), ax[0], ax[1]); mv($('#gpR'), ax[2], ax[3]);
      $('#gpLT').style.transform = `scaleX(${gp.buttons[6]?.value || 0})`; $('#gpRT').style.transform = `scaleX(${gp.buttons[7]?.value || 0})`;
      $$('#gpBtns i').forEach(i => i.classList.toggle('on', !!gp.buttons[+i.dataset.gb]?.pressed));
      const l = Math.hypot(ax[0] || 0, ax[1] || 0), r = Math.hypot(ax[2] || 0, ax[3] || 0), idle = l < .3 && r < .3;
      $('#gpDrift').textContent = `${gp.id.split('(')[0].trim().slice(0, 28)} · Drift L ${l.toFixed(2)} · R ${r.toFixed(2)}${idle ? (Math.max(l, r) > .08 ? ' · Drift erkannt!' : ' · OK') : ''}`;
    }
    gpRaf = requestAnimationFrame(loop);
  };
  loop();
}
function gpRumble() { const gp = [...navigator.getGamepads()].find(Boolean); gp?.vibrationActuator?.playEffect('dual-rumble', { duration: 700, strongMagnitude: 1, weakMagnitude: .6 }).catch(() => { }); }
window.gpRumble = gpRumble;
let ntT = 'cfx';
/** Before a test: the result tiles empty and a row of faint bars, so the card does not show a gap. */
function ntIdle() {
  ['#ntAvg', '#ntJit', '#ntLoss'].forEach(s => { const el = $(s); if (el) { el.textContent = '--'; el._v = 0; } });
  if ($('#ntBars')) $('#ntBars').innerHTML = Array.from({ length: 20 }, (_, i) => `<i class="idle" style="height:${30 + Math.round(Math.abs(Math.sin(i * 1.7)) * 45)}%"></i>`).join('');
  if ($('#ntTxt')) $('#ntTxt').textContent = 'Noch nicht gemessen · dauert ca. 3 s';
}
document.addEventListener('click', e => { const b = e.target.closest('[data-nt]'); if (!b) return; ntT = b.dataset.nt; $$('#ntTarget button').forEach(x => x.classList.toggle('on', x === b)); $('#ntHost').classList.toggle('hidden', ntT !== 'custom'); ntIdle(); });
async function netTest() {
  const target = ntT === 'custom' ? $('#ntHost').value.trim() : ntT;
  if (ntT === 'custom' && !/^[\w.\-]+:\d{1,5}$/.test(target)) return toast('Adresse prüfen', 'Format IP:Port, z. B. 123.45.67.89:30120', false);
  const btn = $('#ntBtn'); btn.disabled = true; $('#ntTxt').textContent = 'Messe…'; $('#ntBars').innerHTML = '';
  try {
    const r = await api('net.test', { target });
    if (!r.ok) { $('#ntAvg').textContent = '--'; $('#ntJit').textContent = '--'; $('#ntLoss').innerHTML = '100<small>%</small>'; $('#ntTxt').textContent = 'Keine Verbindung'; return; }
    countTo($('#ntAvg'), r.avg, '<small>ms</small>'); $('#ntJit').innerHTML = `${nf1.format(r.jitter)}<small>ms</small>`; $('#ntLoss').innerHTML = `${r.loss}<small>%</small>`;
    const mx = Math.max(...r.samples, 1);
    $('#ntBars').innerHTML = r.samples.map((s, i) => `<i style="height:${Math.max(8, s / mx * 100)}%;animation-delay:${i * 25}ms" title="${s} ms"></i>`).join('') + Array.from({ length: r.loss / 5 }, () => '<i class="lost" style="height:100%"></i>').join('');
    const good = r.avg < 60 && r.jitter < 6 && r.loss === 0, ok = r.avg < 110 && r.jitter < 15 && r.loss <= 5;
    $('#ntTxt').innerHTML = good ? '<span style="color:var(--good)">Top für Hitreg</span>' : ok ? '<span style="color:var(--warn)">Spielbar, leichte Schwankungen</span>' : '<span style="color:var(--bad)">Schlecht für Hitreg: Netzwerk-Tweaks anwenden, LAN statt WLAN</span>';
  } catch (e) { toast('Test fehlgeschlagen', e.message, false); }
  finally { btn.disabled = false; }
}
window.netTest = netTest;

/* ======================= THUMBNAIL QUEUE ======================= */
// one thumbnail at a time, so big video folders never block the app
const thumbQ = []; let thumbBusy = false;
function thumb(cmd, args, img) {
  thumbQ.push([cmd, args, img]); if (!thumbBusy) thumbNext();
}
async function thumbNext() {
  const job = thumbQ.shift(); if (!job) { thumbBusy = false; return; }
  thumbBusy = true; const [cmd, args, img] = job;
  if (img.isConnected) {
    try { const r = await api(cmd, args); if (r && r.url) { img.src = r.url; img.classList.add('ok'); } else img.classList.add('none'); } catch { img.classList.add('none'); }
  }
  setTimeout(thumbNext, 15);
}

/* ======================= GTA BACKGROUND ======================= */
// official GTA V screenshots in Full HD; "auto" follows the time of day
const LA = { day: 'img/ls-vinewood.jpg', dusk: 'img/ls-skyline.jpg', night: 'img/ls-night.jpg' };
const LA_ALL = [['auto', 'Automatisch', null], ['skyline', 'Downtown', 'img/ls-skyline.jpg'], ['vinewood', 'Vinewood', 'img/ls-vinewood.jpg'], ['night', 'Nachtfahrt', 'img/ls-night.jpg'], ['chase', 'Verfolgung', 'img/ls-chase.jpg'],
  ['hills', 'Blaine County', 'img/ls-hills.jpg'], ['chiliad', 'Mount Chiliad', 'img/ls-chiliad.jpg'], ['boulevard', 'Boulevard', 'img/ls-boulevard.jpg'], ['sandy', 'Sandy Shores', 'img/ls-sandy.jpg']];
const LA_PICS = LA_ALL.filter(x => x[2]).map(x => x[2]);
// which height of each picture matters most (%): the wide banner only shows a strip of it
const LA_FOCUS = { 'img/ls-skyline.jpg': 30, 'img/ls-vinewood.jpg': 24, 'img/ls-night.jpg': 56, 'img/ls-chase.jpg': 42, 'img/ls-hills.jpg': 38, 'img/ls-chiliad.jpg': 42, 'img/ls-boulevard.jpg': 62, 'img/ls-sandy.jpg': 55 };
const ownUrl = () => null;
const bgUrl = () => (LA_ALL.find(x => x[0] === S.settings.bgPreset)?.[2]) || LA[timeOfDay()];
/** A fixed picture per name, so cards without their own preview do not all look the same. */
const picFor = id => { let h = 0; for (const c of String(id)) h = (h * 31 + c.charCodeAt(0)) >>> 0; return LA_PICS[h % LA_PICS.length]; };
function applyBg() {
  const u = bgUrl();
  // the anime theme brings its own painted scene, unless a particular photo was picked in the settings
  const painted = CUR_THEME === 'anime' && !LA_ALL.find(x => x[0] === S.settings.bgPreset)?.[2];
  const own = painted ? `<div class="own-bg" style="background-image:${ANIME_BG ||= animeScene()};background-position:center 66%"></div>`
    : `<div class="own-bg" style="background-image:url('${u}');background-position:center ${LA_FOCUS[u] ?? 50}%"></div>`;
  $('#bnScene').innerHTML = own;
  $('#spScene').innerHTML = own;
  xhScene('day');
  $$('.dpimg').forEach(d => d.style.backgroundImage = u ? `url('${u}')` : '');
  $$('.dpre i').forEach(d => d.style.backgroundImage = u ? `url('${u}')` : '');
  $$('.rsp i').forEach(i => i.style.backgroundImage = `url('${u}')`);
}
function xhScene(k) {
  const sc = $('#xhScene'); let el = sc.querySelector('.own-bg');
  if (!el) { sc.insertAdjacentHTML('afterbegin', '<div class="own-bg"></div>'); el = sc.querySelector('.own-bg'); }
  el.style.backgroundImage = `url('${k === 'own' ? ownUrl() : LA[k] || LA.day}')`;
  $$('#xhBg button').forEach(b => b.classList.toggle('on', b.dataset.bg === k));
  xhPlace();
}
async function bgGrid() {
  const g = $('#bgGrid'), cur = LA_ALL.some(x => x[0] === S.settings.bgPreset) ? S.settings.bgPreset : 'auto';
  // "Automatisch" shows what it really is: the painted scene in the anime theme, otherwise the photo for the time of day
  const auto = CUR_THEME === 'anime' ? (ANIME_BG ||= animeScene()).slice(5, -2) : LA[timeOfDay()];
  g.innerHTML = LA_ALL.map(([k, n, src]) => `<button class="bgc ${cur === k ? 'on' : ''} ${src ? '' : 'auto'}" data-bgp="${k}"><img alt="" class="ok" src="${src || auto}">${src ? '' : `<em>${ico('sparkle')}<small>${CUR_THEME === 'anime' ? 'Bild vom Theme' : 'Wechselt mit der Tageszeit'}</small></em>`}<span>${n}</span></button>`).join('');
}
document.addEventListener('click', async e => {
  const pr = e.target.closest('[data-bgp]');
  if (pr) {
    S.settings.bgPreset = pr.dataset.bgp; api('settings.set', { key: 'bgPreset', value: S.settings.bgPreset });
    applyBg(); bgGrid(); return;
  }
  const b = e.target.closest('[data-bgi]'); if (!b) return;
  const it = $('#bgGrid')._items[+b.dataset.bgi];
  b.classList.add('busy');
  const r = await api('bg.set', it.k === 'rel' ? { rel: it.v } : { path: it.v });
  b.classList.remove('busy');
  if (r.error) return toast('Bild nicht übernommen', r.error, false);
  S.settings.bgVersion = r.v; applyBg(); bgGrid(); toast('GTA-Hintergrund gesetzt', 'Startseite, Ladebildschirm und Vorschauen nutzen jetzt dein Bild');
});
async function bgPick() { const r = await api('bg.pick'); if (r.cancelled) return; if (r.error) return toast('Bild nicht übernommen', r.error, false); S.settings.bgVersion = r.v; applyBg(); toast('GTA-Hintergrund gesetzt', ''); }
async function bgClear() { await api('bg.clear'); S.settings.bgVersion = 0; applyBg(); bgGrid(); }
window.bgPick = bgPick; window.bgClear = bgClear;

/* ======================= SETTINGS TABS ======================= */
function setTab(t) {
  $$('#setTabs .tab').forEach(b => b.classList.toggle('on', b.dataset.st === t));
  $$('.stab').forEach(s => s.classList.toggle('hidden', s.id !== 'st-' + t));
  if (t === 'design') bgGrid();
  if (t === 'account') accLoad();
  if (t === 'general') { dcRender(); dcLoad(); }
  // (the tab itself fades in through its own animation; the whole page does not come in again)
}
window.setTab = setTab;
document.addEventListener('click', e => { const t = e.target.closest('#setTabs .tab'); if (t) setTab(t.dataset.st); });

/* ======================= RESHADE ======================= */
let RS = { installed: false, presets: [], preset: null };
async function rsUninstall() { if (!await confirmBox('ReShade entfernen?', 'ReShade, Shader und Looks werden aus FiveM gelöscht.', { ok: 'Entfernen', danger: true })) return; const r = await api('reshade.uninstall'); toast(r.error ? 'Nicht entfernt' : 'ReShade entfernt', r.error || '', !r.error); RS = await api('reshade.status'); libRender(); }
window.rsUninstall = rsUninstall;

/* ======================= MOD COLLECTION ======================= */
const COLL = [
  ['minimap', 'Satellit-Minimap', 'Echte Satellitenbilder statt Straßenkarte', 'fivem satellite minimap rpf'],
  ['minimap', 'Dunkle Minimap', 'Dark-Mode-Karte, schont die Augen nachts', 'fivem dark minimap rpf'],
  ['minimap', 'Atlas-Minimap', 'Klassischer Atlas-Look mit Straßennamen', 'fivem atlas minimap rpf'],
  ['minimap', 'Farbige Minimap', 'Kräftige Farben, Straßen gut lesbar', 'fivem colored minimap rpf'],
  ['minimap', 'Postal-Map', 'Postleitzahlen für RP-Server', 'fivem postal map minimap rpf'],
  ['kill', 'Kill-Effekte', 'Bildschirm-Effekte bei Kills', 'fivem kill effect rpf'],
  ['kill', 'Blood FX', 'Realistischeres Blut bei Treffern', 'fivem blood fx rpf'],
  ['kill', 'Tracer & Mündungsfeuer', 'Sichtbare Schüsse, mehr Feedback', 'fivem tracer muzzle flash rpf'],
  ['gfx', 'Grafik-Packs', 'Licht, Farben, Atmosphäre', 'fivem graphics pack rpf'],
  ['gfx', 'Wetter & Timecycle', 'Andere Tageszeiten-Stimmung', 'fivem timecycle weather mod rpf'],
  ['sound', 'Waffen-Sounds', 'Wuchtigere Schüsse', 'fivem gunshot sounds rpf'],
  ['sound', 'Motor-Sounds', 'Echte Motorsounds für Autos', 'fivem engine sound pack rpf'],
];
function collHtml(filter) {
  return COLL.filter(c => !filter || c[0] === filter).map(([cat, n, d, q]) => `<div class="card hov ccard"><div class="qi">${ico(MOD_CATS[cat][1])}</div><b>${n}</b><span>${d}</span><div class="row" style="margin-top:12px;gap:6px;flex-wrap:wrap"><button class="btn sm p" onclick="modBrowse('https://www.gta5-mods.com/search/${encodeURIComponent(q.replace(/ ?(fivem|rpf) ?/g, ' ').trim())}','${cat}')">${ico('globe')}GTA5-Mods</button><button class="btn sm" onclick="modBrowse('https://forum.cfx.re/search?q=${encodeURIComponent(q)}','${cat}')">${ico('globe')}Cfx.re</button></div></div>`).join('');
}
async function dlList(target, filter) {
  let l = []; try { l = await api('mods.downloads'); } catch { }
  if (filter) l = l.filter(x => x.cat === filter || x.cat === 'other');
  const el = $(target); if (!el) return;
  el.innerHTML = l.length ? l.map(f => `<div class="mod dl"><div class="ti mico">${ico(MOD_CATS[f.cat]?.[1] || 'puzzle')}</div><div class="tx"><b>${esc(f.name)}</b><p>${bytes(f.size)} · ${new Date(f.date).toLocaleDateString('de-DE')} · ${esc(f.what || (f.rpfs > 0 ? f.rpfs + ' Mod-Datei(en)' : 'Archiv'))}</p></div><select class="in sel" data-dlcat="${esc(f.name)}">${Object.entries(MOD_CATS).map(([k, [n]]) => `<option value="${k}" ${k === (filter || f.cat) ? 'selected' : ''}>${n}</option>`).join('')}</select><button class="btn o sm" data-dl="${esc(f.name)}">${ico('download')}Installieren</button></div>`).join('')
    : emptyBox('download', 'Noch nichts Neues', 'Lad ein Pack in Discord oder im Browser herunter, es erscheint hier von selbst.');
}
document.addEventListener('click', async e => {
  const b = e.target.closest('[data-dl]'); if (!b) return;
  if (!S.settings.modsAck) return modInstall('__dl');
  const name = b.dataset.dl, cat = b.parentElement.querySelector('[data-dlcat]').value;
  b.disabled = true; b.innerHTML = `${ico('refresh', 'i spin')}Installiere…`;
  const r = await modRun('mods.installDl', { name, cat });
  if (r.error || r.cancelled) { if (r.error) toast('Nicht installiert', r.error, false); b.disabled = false; b.innerHTML = `${ico('download')}Installieren`; }
  else { modReport(r); modList(); }
});
function modBrowse(url, cat) {
  if (!S.settings.modsAck) return modInstall('__browse');
  api('mods.browse', { url, cat, accent: getComputedStyle(document.documentElement).getPropertyValue('--a').trim() });
  toast('Mod-Browser geöffnet', 'Klick auf Download, die Mod wird automatisch installiert');
}
window.modBrowse = modBrowse;
// packs from Discord: open Discord in the mod browser, or install the file behind a copied link
let dcCat = 'sound';
document.addEventListener('click', e => { const b = e.target.closest('#dcCat button'); if (!b) return; dcCat = b.dataset.v; $$('#dcCat button').forEach(x => x.classList.toggle('on', x === b)); });
// installs everything listed under "new in your downloads", one after the other, each as the category chosen in its row
window.dcAll = async () => {
  if (!S.settings.modsAck) return modInstall('__dl');
  const rows = $$('#modDl [data-dl]'); if (!rows.length) return toast('Nichts zu installieren', 'Lade erst ein Pack in Discord herunter', false);
  const btn = $('#dcAllBtn'); btn.disabled = true; const all = { notes: [], off: [], mods: [] }; let fail = '';
  for (const b of rows) {
    // the category of the row only counts when the content does not tell; "Sonstiges" falls back to the choice above
    const sel = b.parentElement.querySelector('[data-dlcat]'), cat = sel && sel.value !== 'other' ? sel.value : dcCat;
    const r = await modRun('mods.installDl', { name: b.dataset.dl, cat });
    if (r.error) fail = `${b.dataset.dl}: ${r.error}`;
    else if (!r.cancelled) { all.notes.push(...(r.notes || [])); all.off.push(...(r.off || [])); all.mods.push(...(r.mods || [])); }
  }
  btn.disabled = false;
  if (all.mods.length) modReport(all);
  if (fail) toast(all.mods.length ? 'Nicht alles installiert' : 'Nicht installiert', fail, false);
  modList(); dlList('#modDl');
};
window.dcOpen = () => modBrowse('https://discord.com/channels/@me', dcCat);
window.dcLink = () => {
  const el = $('#dcLink'), u = el.value.trim();
  if (!/^https:\/\/(cdn\.discordapp\.com|media\.discordapp\.net|[\w.-]+)\/\S+/i.test(u)) return toast('Kein Link', 'Kopier in Discord den Link der Datei (Rechtsklick → Link kopieren)', false);
  if (!/\.(rpf|zip|rar|7z|oiv)(\?|$)/i.test(u)) return toast('Keine Mod-Datei', 'Der Link muss auf eine .rpf, .zip, .rar, .7z oder .oiv zeigen', false);
  modBrowse(u, dcCat); el.value = '';
};
async function modsRefresh() {
  await libLoad(); RSC = null; libRender();
  if (curPage === 'mods') { dlList('#modDl'); modList(); }
  if (curPage === 'minimap') { dlList('#mmDl', 'minimap'); modList(); }
}
setInterval(() => { if (curPage === 'mods') dlList('#modDl'); if (curPage === 'minimap') dlList('#mmDl', 'minimap'); }, 6000);

/* ======================= CURRENT SERVER ======================= */
let CUR = null;
function curHtml(c) {
  const s = c.server || {}, initial = (s.name || '?').replace(/\^\d/g, '').trim()[0] || '?';
  const stat = (k, v, unit = '', extra = '', cls = '') => v == null || v === '' ? '' : `<div${cls ? ` class="${cls}"` : ''}><span>${k}</span><b>${esc(v)}${unit ? `<small>${unit}</small>` : ''}</b>${extra}</div>`;
  const pct = s.max ? Math.min(100, Math.round(s.players * 100 / s.max)) : 0;
  const tags = (s.allTags && s.allTags.length ? s.allTags : s.tags || []).slice(0, 10);
  return `<div class="cs-bn ${s.banner ? '' : 'nobn'}" ${s.banner ? `style="background-image:url('${esc(s.banner)}')"` : ''}></div>
    <div class="cs-body">
      <div class="cs-top">
        <div class="cs-icon" ${s.icon ? `style="background-image:url('${esc(s.icon)}')"` : ''}>${s.icon ? '' : esc(initial)}</div>
        <div class="cs-main">
          <div class="row" style="gap:8px">${c.connected ? '<span class="chip g live">Verbunden</span>' : '<span class="chip">Zuletzt gespielt</span>'}${s.locale ? `<span class="chip">${esc(s.locale)}</span>` : ''}${s.online === false ? '<span class="chip r">Offline</span>' : ''}</div>
          <h2 class="cs-name">${fivemName(s.name || c.url)}</h2>
          <p class="cs-desc">${esc(s.desc || '')}</p>
        </div>
      </div>
      <div class="cs-tags">${tags.map(t => `<span class="chip">${esc(t)}</span>`).join('')}</div>
      <div class="cs-stats">${stat('Spieler', s.max ? s.players : null, s.max ? ' / ' + s.max : '', `<i class="cs-pb"><i style="transform:scaleX(${pct / 100})"></i></i>`)}${stat('Ping', s.ping != null ? s.ping : null, ' ms')}${stat('Build', s.build ? 'b' + s.build : null)}${stat('OneSync', s.onesync ? 'An' : null)}${stat('Ressourcen', s.resources || null)}${stat('Betreiber', s.owner, '', '', 'tx')}</div>
      <div class="cs-act">
        ${c.fav ? `<button class="btn sm" disabled>${ico('check')}In Favoriten</button>` : `<button class="btn sm" onclick="curFav()">${ico('plus')}Zu Favoriten</button>`}
        ${s.discord ? `<button class="btn sm dc" onclick="api('open',{target:'${esc(s.discord)}'})"><svg class="i" viewBox="0 0 24 24"><path d="M8 12h.01M16 12h.01M7.5 7.5c3-1.3 6-1.3 9 0M7.5 16.5c3 1.3 6 1.3 9 0M15.5 17l1.5 3c2-.7 3.5-1.7 5-3-.4-4.2-1.4-8-3.5-11-1.4-.7-2.9-1.2-4.5-1.5L13.5 6M8.5 17 7 20c-2-.7-3.5-1.7-5-3 .4-4.2 1.4-8 3.5-11C6.9 5.3 8.4 4.8 10 4.5L10.5 6"/></svg>Discord</button>` : ''}
        ${s.website ? `<button class="btn sm" onclick="api('open',{target:'${esc(s.website)}'})">${ico('globe')}Website</button>` : ''}
        <button class="btn sm" onclick="copyCmd('${esc(s.connect || c.url)}')">${ico('link')}Link kopieren</button>
        ${c.connected ? '' : `<button class="btn sm p" onclick="launch('${esc(s.connect || c.url)}')">${ico('play')}Verbinden</button>`}
      </div>
    </div>`;
}
async function curServer(force) {
  let c; try { c = await api('server.current', { force: !!force }); } catch { return; }
  const show = c && c.found;
  ['#curSrv', '#curSrv2'].forEach(id => { const el = $(id); if (!el) return; el.classList.toggle('hidden', !show); if (show && JSON.stringify(c) !== JSON.stringify(CUR)) el.innerHTML = curHtml(c); });
  if (show && c.connected && (!CUR || CUR.key !== c.key || !CUR.connected)) feed('Verbunden mit ' + (c.server?.name || c.url).replace(/\^\d/g, ''));
  CUR = show ? c : null;
}
async function curFav() {
  if (!CUR) return;
  try { await api('servers.add', { q: CUR.url }); toast('Zu Favoriten hinzugefügt', (CUR.server?.name || '').replace(/\^\d/g, '')); S.servers = []; CUR = null; curServer(); }
  catch (e) { toast('Nicht hinzugefügt', e.message, false); }
}
window.curFav = curFav;
setInterval(() => { if (curPage === 'home' || curPage === 'servers') curServer(); }, 15000);

/* ======================= CONTROLLER DISPLAY ======================= */
const PAD = { cfg: { mode: 'auto', layout: 'auto', body: 'black', color: 'theme', corner: 'br', size: 75, opacity: 90, key: 0, accent: '#FF7A3D' }, r: null, state: null, active: false };
const PAD_COLORS = ['#FF3B4E', '#FF7A18', '#F5C451', '#3DDC6B', '#3D8BFF', '#8B5CF6', '#F472B6', '#FFFFFF', '#18181B'];
const PAD_CNAME = { '#FF3B4E': 'Rot', '#FF7A18': 'Orange', '#F5C451': 'Gelb', '#3DDC6B': 'Grün', '#3D8BFF': 'Blau', '#8B5CF6': 'Lila', '#F472B6': 'Pink', '#FFFFFF': 'Weiß', '#18181B': 'Schwarz' };
const accHex = () => getComputedStyle(document.documentElement).getPropertyValue('--a').trim();
const padAcc = () => PAD.cfg.color.startsWith('#') ? PAD.cfg.color : accHex();
function padDraw() { if (PAD.r) PAD.r.draw(PAD.state, { ...PAD.cfg, accent: padAcc() }); }
function padUI() {
  const c = PAD.cfg;
  [['#padMode', c.mode], ['#padLayout', c.layout], ['#padBody', c.body], ['#padOpacity', String(c.opacity)]].forEach(([s, v]) => $$(s + ' button').forEach(b => b.classList.toggle('on', b.dataset.v === v)));
  $$('#padCorner button').forEach(b => b.classList.toggle('on', b.dataset.v === c.corner));
  const cn = $('#padCornerV'); if (cn) cn.textContent = { tl: 'Oben links', tr: 'Oben rechts', bl: 'Unten links', br: 'Unten rechts' }[c.corner] || '';
  const pc = $('#padCorner'); if (pc) { pc.style.setProperty('--psz', (c.size / 100).toFixed(2)); pc.style.setProperty('--pop', (c.opacity / 100).toFixed(2)); }
  // round swatches: the first follows the theme, the last opens a colour picker; the name of the chosen one below
  const theme = !c.color.startsWith('#'), col = c.color.toUpperCase(), own = !theme && !PAD_COLORS.includes(col);
  $('#padColors').innerHTML = `<button class="xcol xtheme ${theme ? 'on' : ''}" data-pc="theme" title="Theme-Farbe" aria-label="Theme-Farbe"></button>` +
    PAD_COLORS.map(x => `<button class="xcol ${col === x ? 'on' : ''}" data-pc="${x}" style="background:${x}" title="${PAD_CNAME[x]}" aria-label="${PAD_CNAME[x]}"></button>`).join('') + `<label class="xcustom ${own ? 'on' : ''}" title="Eigene Farbe"><input type="color" id="padPick" value="${c.color.startsWith('#') ? c.color : '#FF7A3D'}"></label>`
    + `<span class="xc-n">${theme ? '<b>Theme-Farbe</b> · wechselt mit deinem Theme' : own ? `<b>Eigene Farbe</b> · ${esc(col)}` : `<b>${PAD_CNAME[col]}</b>`}</span>`;
  $('#padSize').value = c.size; $('#padSize').style.setProperty('--p', (c.size - 40) / 120 * 100 + '%'); $('#padSizeV').textContent = c.size + '%';
  $('#padKey').innerHTML = KEYS.map(([n, v]) => `<button class="${c.key === v ? 'on' : ''}" data-pk="${v}">${n}</button>`).join('');
  $('#navPad').classList.toggle('hidden', c.mode === 'off');
  $('#navPad').textContent = c.mode === 'on' ? 'AN' : 'AUTO';
  padDraw();
}
let padT = 0, padArtKey = null;
function padPush() {
  clearTimeout(padT);
  padT = setTimeout(async () => {
    const args = { cfg: { ...PAD.cfg, accent: padAcc() } };
    const key = PAD.cfg.body + padAcc();
    if (padArtKey !== key && PAD.cfg.mode !== 'off') {   // pictures only change with body / colour
      padArtKey = key;
      args.packs = { xbox: await PadArt.overlayPack('xbox', PAD.cfg.body, padAcc()), ps: await PadArt.overlayPack('ps', PAD.cfg.body, padAcc()) };
    }
    const r = await api('pad.set', args).catch(() => null);
    if (r && r.keyOk === false) toast('Taste belegt', 'Andere Taste wählen', false);
  }, 80);
}

document.addEventListener('click', e => {
  const s = e.target.closest('#padMode button,#padLayout button,#padBody button,#padOpacity button,#padCorner button');
  if (s) {
    const k = { padMode: 'mode', padLayout: 'layout', padBody: 'body', padOpacity: 'opacity', padCorner: 'corner' }[s.parentElement.id];
    if (k === 'mode') toast({ auto: 'Controller-Anzeige automatisch', on: 'Controller-Anzeige immer an', off: 'Controller-Anzeige aus' }[s.dataset.v], s.dataset.v === 'auto' ? 'Erscheint, sobald du den Controller benutzt' : ''); PAD.cfg[k] = k === 'opacity' ? +s.dataset.v : s.dataset.v; padUI(); padPush(); return; }
  const c = e.target.closest('[data-pc]'); if (c) { PAD.cfg.color = c.dataset.pc; padUI(); padPush(); return; }
  const k = e.target.closest('[data-pk]'); if (k) { PAD.cfg.key = +k.dataset.pk; padUI(); padPush(); }
});
document.addEventListener('input', e => {
  if (e.target.id === 'padSize') { PAD.cfg.size = +e.target.value; e.target.style.setProperty('--p', (PAD.cfg.size - 40) / 120 * 100 + '%'); $('#padSizeV').textContent = PAD.cfg.size + '%'; $('#padCorner')?.style.setProperty('--psz', (PAD.cfg.size / 100).toFixed(2)); padPush(); }
  if (e.target.id === 'padPick') { PAD.cfg.color = e.target.value.toUpperCase(); padDraw(); padPush(); }
});
document.addEventListener('change', e => { if (e.target.id === 'padPick') padUI(); });
function padState(d, active) {
  PAD.state = d;
  const n = $('#padName'); if (n) { n.textContent = d && d.c ? d.name : 'Kein Controller'; n.className = 'chip ' + (d && d.c ? 'g' : ''); }
  padDraw();
}
async function initPad(s) {
  Object.assign(PAD.cfg, s.pad || {});
  if (!['auto', 'on', 'off'].includes(PAD.cfg.mode)) PAD.cfg.mode = 'auto';
  if (PAD.cfg.color === 'orig') PAD.cfg.color = 'theme';
  PAD.r = new PadArt.Pad($('#padPrev'));
  padUI(); padPush();
}

/* ======================= MOD LIBRARY ======================= */
const LIB_URL = 'https://raw.githubusercontent.com/Ky3ls/orbittweak/main/mods.json';
const LIB_CATS = [['all', 'Alle', 'layers'], ['reshade', 'ReShade-Looks', 'sparkle'], ['kill', 'Kill-Effekte', 'crosshair'], ['minimap', 'Minimap', 'map'], ['gfx', 'Grafik-Packs', 'sun'], ['sound', 'Sounds', 'activity'], ['other', 'Sonstiges', 'puzzle']];
const LIB = { items: [], installed: new Set(), cat: 'all', q: '', sort: 'pop', busy: {}, api: '' };
/** One item of a category, for the badge on a single card. */
const LIB_ONE = { reshade: 'ReShade-Look', kill: 'Kill-Effekt', minimap: 'Minimap', gfx: 'Grafik-Pack', sound: 'Sound', other: 'Mod' };
async function libLoad() {
  let data = null;
  try { const c = new AbortController(); setTimeout(() => c.abort(), 4000); data = await (await fetch(LIB_URL + '?t=' + Math.floor(Date.now() / 3e5), { signal: c.signal })).json(); } catch { }
  if (!data || !Array.isArray(data.mods)) try { data = await (await fetch('mods.json')).json(); } catch { data = { mods: [] }; }
  LIB.api = typeof data.api === 'string' ? data.api : '';
  let srvMods = [];
  if (LIB.api) try { const r = await api('srv', { api: LIB.api, method: 'GET', path: '/api/mods' }); srvMods = (r && r.mods) || []; } catch { }
  const seen = new Set();
  LIB.items = [...data.mods, ...srvMods].filter(m => m && m.id && m.name && !seen.has(m.id) && seen.add(m.id) && (m.type === 'reshade' || (m.type === 'file' && /^https:\/\//.test(m.url || ''))));
  try { LIB.installed = new Set((await api('mods.lib')).installed || []); } catch { }
  try { RS = await api('reshade.status'); } catch { }
}
const libState = m => m.type === 'reshade' ? (RS.installed ? (RS.preset === m.preset ? 'active' : 'ready') : 'none') : (LIB.installed.has(m.id) ? 'installed' : 'none');
function libCard(m) {
  const st = libState(m), busy = LIB.busy[m.id];
  // ReShade looks share one picture so they can be compared, other mods get different ones
  const thumb = m.img ? `style="background-image:url('${esc(m.img)}')"` : `style="background-image:url('${m.type === 'reshade' ? bgUrl() : picFor(m.id)}')"`;
  const cat = LIB_CATS.find(c => c[0] === m.cat) || LIB_CATS[6];
  const btn = busy != null ? `<button class="btn sm" disabled>${ico('refresh', 'i spin')}${busy > 0 ? busy + ' %' : 'Installiere…'}</button>`
    : st === 'active' ? `<button class="btn sm ok" disabled>${ico('check')}Aktiv</button>`
    : st === 'ready' ? `<button class="btn o sm" data-lib="${esc(m.id)}">${ico('zap')}Aktivieren</button>`
    : st === 'installed' ? `<button class="btn sm d" data-librm="${esc(m.id)}">${ico('trash')}Entfernen</button>`
    : `<button class="btn o sm" data-lib="${esc(m.id)}">${ico('download')}Installieren</button>`;
  const foot = m.type === 'reshade' ? (st === 'active' ? 'Aktiv im Spiel' : st === 'ready' ? 'Sofort nutzbar' : 'Mit ReShade') : st === 'installed' ? 'Installiert' : m.size ? bytes(m.size) : 'Mod-Datei';
  return `<div class="lcard ${st === 'active' || st === 'installed' ? 'on' : ''}">
    <div class="lpic"><div class="lthumb ${m.type === 'reshade' && !m.img ? 'rsf-' + esc(m.preset) : ''}" ${thumb}></div>
    <div class="ltop"><span class="lcat">${ico(cat[2])}${LIB_ONE[cat[0]] || cat[1]}</span>${m.popular >= 85 ? '<span class="lhot">Beliebt</span>' : ''}</div></div>
    <div class="lbody"><b>${esc(m.name)}</b><span class="lby">von ${esc(m.author || 'unbekannt')}${m.addedBy ? ' · eingereicht' : ''}</span><p>${esc(m.desc || '')}</p>
      <div class="ltags">${(m.tags || []).slice(0, 4).map(t => `<span>${esc(t)}</span>`).join('')}</div>
      <div class="lact"><span class="lsize st-${st}">${foot}</span>${btn}</div></div></div>`;
}
function libList(cat) {
  const q = LIB.q.trim().toLowerCase();
  let l = LIB.items.filter(m => (cat === 'all' || m.cat === cat) && (!q || [m.name, m.desc, m.author, ...(m.tags || [])].join(' ').toLowerCase().includes(q)));
  l.sort(LIB.sort === 'az' ? (a, b) => a.name.localeCompare(b.name) : LIB.sort === 'new' ? (a, b) => (b.added || '').localeCompare(a.added || '') : (a, b) => (b.popular || 0) - (a.popular || 0));
  return l;
}
function libRender() {
  // while searching the chips count the hits, so they match the grid below
  if ($('#libCats')) $('#libCats').innerHTML = LIB_CATS.map(([k, n, i]) => { const all = k === 'all' ? LIB.items.length : LIB.items.filter(m => m.cat === k).length; if (!all && k !== LIB.cat) return ''; const c = LIB.q.trim() ? libList(k).length : all; return `<button class="lchip ${LIB.cat === k ? 'on' : ''} ${c ? '' : 'zero'}" data-lc="${k}">${ico(i)}${n}<span>${c}</span></button>`; }).join('');
  const empty = cat => LIB.q ? emptyBox('search', `Nichts gefunden für „${esc(LIB.q)}“`, 'Probier ein anderes Wort, zum Beispiel pvp, nacht oder kill.', `<button class="btn" onclick="libClear()">${ico('undo')}Suche zurücksetzen</button>`)
    : cat === 'minimap' ? emptyBox('map', 'Noch keine fertigen Minimaps', 'Aus jedem Bild baust du dir in einer Minute deine eigene.', `<button class="btn p" onclick="modTab('mm')">${ico('map')}Eigene Minimap erstellen</button>`)
    : emptyBox('puzzle', 'Noch nichts in dieser Kategorie', 'Neue Mods kommen von selbst, sobald sie in der Bibliothek stehen. Eigene Dateien gehen über „Datei installieren“.');
  if ($('#libGrid')) { const l = libList(LIB.cat); $('#libGrid').innerHTML = l.length ? l.map(libCard).join('') : empty(LIB.cat); }
  if ($('#mmLib')) { const l = libList('minimap'); $('#mmLib').innerHTML = l.length ? l.map(libCard).join('') : empty('minimap'); }
  rsSettings();
}
/** One quiet empty state for the mods page: icon, a title, one line, an optional action. */
function emptyBox(icon, title, text, act = '') { return `<div class="empty ebox"><span class="en-ic">${ico(icon)}</span><b>${title}</b><span>${text}</span>${act}</div>`; }
function libClear() { LIB.q = ''; if ($('#libSearch')) $('#libSearch').value = ''; libRender(); }
window.libClear = libClear;
/** Empty ReShade tab: straight to the looks in the library. */
function rsPick() { LIB.cat = 'reshade'; LIB.q = ''; if ($('#libSearch')) $('#libSearch').value = ''; modTab('lib'); libRender(); }
window.rsPick = rsPick;
async function libInstall(id) {
  const m = LIB.items.find(x => x.id === id); if (!m) return;
  if (!S.settings.modsAck && m.type !== 'reshade') return modInstall('__dl');
  LIB.busy[id] = 0; libRender();
  try {
    if (m.type === 'reshade') {
      if (!RS.installed) {
        if (!S.settings.modsAck) { delete LIB.busy[id]; libRender(); return modInstall('gfx', false, true); }
        RS.preset = m.preset;
        const r = await api('reshade.install', { preset: m.preset });
        if (r.error) throw new Error(r.error);
        passed('ReShade installiert', `Look „${m.name}“ aktiv · im Spiel mit Pos1 öffnen`);
      } else {
        const r = await api('reshade.preset', { id: m.preset }); if (r.error) throw new Error(r.error);
        toast(`Look „${m.name}“ aktiv`, 'Ab dem nächsten FiveM-Start');
      }
      RS = await api('reshade.status');
    } else {
      const r = await api('mods.get', { id: m.id, url: m.url, sha256: m.sha256 || '', cat: m.cat, name: m.name });
      if (r.error) throw new Error(r.error);
      LIB.installed.add(m.id); modReport({ ...r, mods: [{ name: m.name, cat: m.cat }] }); modList();
    }
  } catch (e) { toast('Nicht installiert', e.message, false); }
  delete LIB.busy[id]; libRender();
}
async function libRemove(id) {
  const m = LIB.items.find(x => x.id === id);
  if (!await confirmBox('Mod entfernen?', (m ? m.name : id) + ' wird aus FiveM gelöscht.', { ok: 'Entfernen', danger: true })) return;
  const r = await api('mods.remove', { id }); if (r.error) return toast('Nicht entfernt', r.error, false);
  LIB.installed.delete(id); toast('Mod entfernt', m ? m.name : ''); libRender(); modList();
}
document.addEventListener('click', e => {
  const c = e.target.closest('[data-lc]'); if (c) { LIB.cat = c.dataset.lc; libRender(); return; }
  const s = e.target.closest('#libSort button'); if (s) { LIB.sort = s.dataset.v; $$('#libSort button').forEach(b => b.classList.toggle('on', b === s)); libRender(); return; }
  const i = e.target.closest('[data-lib]'); if (i) { libInstall(i.dataset.lib); return; }
  const r = e.target.closest('[data-librm]'); if (r) libRemove(r.dataset.librm);
});
document.addEventListener('input', e => { if (e.target.id === 'libSearch') { LIB.q = e.target.value; libRender(); } });

/* ---- ReShade settings ---- */
const RS_KEYS = [['Aus', 0], ['Pos1', 36], ['Einfg', 45], ['Entf', 46], ['Ende', 35], ['Bild↑', 33], ['Bild↓', 34], ['Druck', 44], ['Rollen', 145], ['F2', 113], ['F9', 120], ['F11', 122]];
let RSC = null;
async function rsSettings() {
  const card = $('#rsSet'); if (!card) return;
  card.classList.toggle('hidden', !RS.installed); $('#rsNone')?.classList.toggle('hidden', !!RS.installed);
  if ($('#mtRs')) $('#mtRs').textContent = RS.installed ? 'Installiert' : 'Nicht installiert';
  if ($('#mtLib')) $('#mtLib').textContent = LIB.items.length + ' Looks & Mods';
  if (!RS.installed) return;
  if (!RSC) try { RSC = await api('reshade.cfg.get'); } catch { RSC = null; }
  if (!RSC || !RSC.found) return;
  $('#rsVer').textContent = RS.version ? 'v' + RS.version : 'installiert';
  const look = LIB.items.find(m => m.type === 'reshade' && m.preset === RS.preset), lk = $('#rsLook');
  if (lk) lk.innerHTML = `<span class="rs-lt ${look && !look.img ? 'rsf-' + esc(look.preset) : ''}" style="background-image:url('${look?.img ? esc(look.img) : bgUrl()}')"></span><div class="rs-lx"><span>Aktiver Look</span><b>${esc(look?.name || RS.preset || 'Keiner')}</b><p>${esc(look?.desc || 'Wähl einen Look in der Bibliothek.')}</p></div><button class="btn" onclick="rsPick()">${ico('layers')}Look wechseln</button>`;
  $$('[data-rk]').forEach(el => {
    const k = el.dataset.rk, cur = +RSC[k] || 0, list = RS_KEYS.filter(([, v]) => k === 'keyNext' || v !== 0);
    // a key set elsewhere (e.g. in the ReShade overlay) is shown as it is, not as the first entry of the list
    if (!list.some(([, v]) => v === cur)) list.unshift([!cur ? 'Aus' : cur >= 112 && cur <= 135 ? 'F' + (cur - 111) : 'Taste ' + cur, cur]);
    el.innerHTML = list.map(([n, v]) => `<option value="${v}" ${cur === v ? 'selected' : ''}>${n}</option>`).join('');
  });
  $('#rsFps').classList.toggle('on', !!RSC.showFps); $('#rsPerf').classList.toggle('on', !!RSC.perf);
}
async function rsSave(patch) {
  Object.assign(RSC, patch);
  const r = await api('reshade.cfg.set', { cfg: patch });
  if (r.error) toast('Nicht gespeichert', r.error, false); else toast('ReShade gespeichert', 'Gilt ab dem nächsten FiveM-Start');
  rsSettings();
}
document.addEventListener('change', e => {
  const s = e.target.closest('select[data-rk]'); if (!s || !RSC) return;
  const k = s.dataset.rk, v = +s.value; const clash = ['keyOverlay', 'keyEffects', 'keyScreenshot', 'keyNext'].find(x => x !== k && v && RSC[x] === v);
  if (clash) { s.value = RSC[k]; return toast('Taste schon belegt', 'Bitte andere Taste wählen', false); }
  rsSave({ [k]: v });
});
document.addEventListener('click', e => {
  if (e.target.closest('#rsFps') && RSC) rsSave({ showFps: !RSC.showFps });
  if (e.target.closest('#rsPerf') && RSC) rsSave({ perf: !RSC.perf });
});

/* ======================= ACCOUNT / ADMIN ======================= */
const ACC = { role: null, loggedIn: false, hwid: '' };
const srv = (method, path, body) => api('srv', { api: LIB.api, method, path, body });
async function accLoad() {
  try { Object.assign(ACC, await api('auth.info')); } catch { }
  if (!LIB.api) try { await libLoad(); } catch { }
  accUI();
  if (ACC.loggedIn && LIB.api) {
    const me = await srv('GET', '/api/me').catch(() => null);
    if (!me || me.error) { ACC.loggedIn = false; ACC.role = null; } else { ACC.role = me.role; ACC.plan = { days: me.days, exp: me.exp }; }
    accUI();
    if (ACC.role === 'admin') admPendingCount();
  }
}
function accUI() {
  const r = ACC.loggedIn ? ACC.role : null;
  $('#accHwid').textContent = ACC.hwid || '…';
  const ar = $('#accRole'); if (ar) { ar.className = 'chip ' + (r === 'admin' ? 'a' : r ? 'g' : ''); ar.textContent = r === 'admin' ? 'Admin' : r === 'editor' ? 'Editor' : r ? 'Nutzer' : 'Nicht angemeldet'; }
  $('#accLogin').classList.toggle('hidden', r === 'admin'); $('#accIn').classList.toggle('hidden', !r);
  $('#accTitle').textContent = r === 'admin' ? 'Lizenz aktiv · Admin' : r === 'editor' ? 'Lizenz aktiv · Editor' : 'Lizenz aktiv';
  $('#accText').textContent = (r === 'admin' ? 'Du kannst Keys erzeugen, Mods direkt live stellen und Einreichungen freigeben.' : r === 'editor' ? 'Du kannst Mods einreichen. Ein Admin gibt sie frei.' : 'OrbitTweak ist auf diesem PC freigeschaltet.')
    + (r ? ' ' + planText(ACC.plan) : '');
  $('#accNoSrv').classList.add('hidden');
  $('#navAdmin').classList.toggle('hidden', r !== 'admin');
  moveInd();
  $('#libAdd').classList.toggle('hidden', !(r === 'admin' || r === 'editor'));
  spProfile();
}
async function accRedeem() {
  const code = $('#accCode').value.trim().toUpperCase();
  if (!/^ORB-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$/.test(code)) return toast('Code prüfen', 'Format: ORB-XXXX-XXXX-XXXX', false);
  const r = await api('auth.redeem', { code });
  if (r.error) return toast('Code nicht angenommen', r.error, false);
  ACC.loggedIn = true; ACC.role = r.role; $('#accCode').value = ''; accUI();
  passed(r.role === 'admin' ? 'Admin freigeschaltet' : 'Zugang freigeschaltet', r.rebound ? 'Neuinstallation erkannt, dein Key läuft auf diesem PC weiter' : 'Code ist jetzt an diesen PC gebunden');
}
async function accLogout() { if (!await confirmBox('Abmelden?', 'OrbitTweak wird gesperrt, bis du deinen Key wieder eingibst.', { ok: 'Abmelden' })) return; await api('auth.logout'); ACC.loggedIn = false; ACC.role = null; accUI(); go('home'); gate('Abgemeldet. Gib deinen Key ein, um weiterzumachen.'); }
async function accCopyId() {
  const id = ACC.hwid || $('#accHwid').textContent.trim(); if (!id || id === '…') return;
  let ok = false;
  try { const r = await api('clip', { text: id }); ok = r && r.ok; } catch { }
  if (!ok) try { await navigator.clipboard.writeText(id); ok = true; } catch { }
  toast(ok ? 'PC-ID kopiert' : 'Kopieren fehlgeschlagen', id, ok);
}
window.accRedeem = accRedeem; window.accLogout = accLogout; window.accCopyId = accCopyId;
document.addEventListener('input', e => { if (e.target.id === 'accCode') { let v = e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, ''); if (v.startsWith('ORB')) v = v.slice(3); v = v.slice(0, 12); e.target.value = v ? 'ORB-' + v.match(/.{1,4}/g).join('-') : ''; } });

/* ---- add mod ---- */
function libAddForm() {
  const cats = LIB_CATS.filter(c => c[0] !== 'all' && c[0] !== 'reshade');
  modal(`<div class="kicker">Bibliothek</div><h1 style="margin-top:8px;font-size:24px">Mod <span class="grad">hinzufügen</span></h1>
    <p style="font-size:12.5px">${ACC.role === 'admin' ? 'Als Admin ist die Mod sofort für alle sichtbar.' : 'Ein Admin prüft die Mod, bevor sie für alle sichtbar wird.'} Nur Mods eintragen, die geteilt werden dürfen.</p>
    <div class="form mt">
      <label>Name<input class="in" id="fmName" maxlength="60" placeholder="z. B. Red Flash Kill-Effekt"></label>
      <label>Kategorie<div class="seg" id="fmCat">${cats.map(([k, n], i) => `<button class="${i === 0 ? 'on' : ''}" data-v="${k}">${n}</button>`).join('')}</div></label>
      <label>Direkter Download-Link (https)<input class="in" id="fmUrl" placeholder="https://…/mod.zip  (.rpf, .zip, .rar, .7z)"></label>
      <label>Vorschaubild-Link (optional)<input class="in" id="fmImg" placeholder="https://…/vorschau.jpg"></label>
      <label>Beschreibung<input class="in" id="fmDesc" maxlength="240" placeholder="Was macht die Mod?"></label>
      <div class="form2"><label>Autor<input class="in" id="fmAuthor" maxlength="40" placeholder="Ersteller der Mod"></label><label>Tags<input class="in" id="fmTags" placeholder="pvp, rot, clean"></label></div>
    </div>
    <div class="act"><button class="btn lg" onclick="closeModal()">Abbrechen</button><button class="btn p lg" id="fmGo" onclick="libAddSend()">${ico('check')}Hinzufügen</button></div>`);
  $('#mbox').style.width = '640px';
}
document.addEventListener('click', e => { const b = e.target.closest('#fmCat button'); if (b) $$('#fmCat button').forEach(x => x.classList.toggle('on', x === b)); });
async function libAddSend() {
  const body = { name: $('#fmName').value, cat: $('#fmCat .on')?.dataset.v || 'other', url: $('#fmUrl').value.trim(), img: $('#fmImg').value.trim(), desc: $('#fmDesc').value, author: $('#fmAuthor').value, tags: $('#fmTags').value };
  if (body.name.trim().length < 3) return toast('Name fehlt', '', false);
  if (!/^https:\/\//.test(body.url)) return toast('Link prüfen', 'Der Download-Link muss mit https:// beginnen', false);
  const b = $('#fmGo'); b.disabled = true; b.innerHTML = `${ico('refresh', 'i spin')}Prüfe Datei…`;
  const r = await srv('POST', '/api/mods', body).catch(e => ({ error: e.message }));
  b.disabled = false; b.innerHTML = `${ico('check')}Hinzufügen`;
  if (r.error) return toast('Nicht hinzugefügt', r.error, false);
  closeModal(); $('#mbox').style.width = '';
  passed(r.status === 'live' ? 'Mod ist live' : 'Mod eingereicht', r.status === 'live' ? 'Für alle in der Bibliothek sichtbar' : 'Wartet auf Freigabe durch einen Admin');
  modsRefresh();
}
window.libAddForm = libAddForm; window.libAddSend = libAddSend;

/* ---- admin panel ---- */
let admTab = 'codes';
function admShow(t) { admTab = t; $$('#admTabs .tab').forEach(b => b.classList.toggle('on', b.dataset.at === t)); ['codes', 'pending', 'mods'].forEach(x => $('#adm-' + x).classList.toggle('hidden', x !== t)); admLoad(); }
document.addEventListener('click', e => { const t = e.target.closest('#admTabs .tab'); if (t) admShow(t.dataset.at); const r = e.target.closest('#admRole button'); if (r) { $$('#admRole button').forEach(x => x.classList.toggle('on', x === r)); if ($('#admRoleHint')) $('#admRoleHint').textContent = ROLE_HINT[r.dataset.v] || ''; } });
/** Last seen as a whole phrase: "gerade eben aktiv", "aktiv vor 5 Min.". */
const ago = s => { if (!s) return 'noch nie aktiv'; const d = Date.now() / 1000 - s; return d < 120 ? 'gerade eben aktiv' : 'aktiv vor ' + (d < 7200 ? Math.round(d / 60) + ' Min.' : d < 172800 ? Math.round(d / 3600) + ' Std.' : Math.round(d / 86400) + ' Tagen'); };
const ROLE_HINT = { user: 'Darf OrbitTweak benutzen.', editor: 'Darf zusätzlich Mods einreichen, ein Admin prüft sie.', admin: 'Darf alles, auch Keys erzeugen und Mods freigeben.' };
async function admPendingCount() {
  const r = await srv('GET', '/api/mods/pending').catch(() => null); const n = (r && r.mods || []).length;
  $('#navPend').textContent = n; $('#navPend').classList.toggle('hidden', !n); $('#admPendC').textContent = n;
}
async function admLoad() {
  if (ACC.role !== 'admin') return;
  if (admTab === 'codes') {
    const r = await srv('GET', '/api/codes'); if (r.error) return toast('Fehler', r.error, false);
    $('#admCount').textContent = r.codes.length;
    // fixed columns: code, copy, role, runtime, note, status (with PC and last seen below), actions
    $('#admCodes').innerHTML = r.codes.length ? '<div class="crow2 crh"><span>Key</span><span></span><span>Rolle</span><span>Laufzeit</span><span>Notiz</span><span>Status</span><span></span></div>' + r.codes.map(c => `<div class="crow2 ${c.revoked ? 'off' : ''}">
      <code class="cc">${esc(c.code)}</code><button class="ib" title="Kopieren" onclick="copyCmd('${esc(c.code)}')">${ico('copy')}</button>
      <span class="c-role"><span class="chip ${c.role === 'admin' ? 'a' : c.role === 'editor' ? 'g' : ''}">${c.role === 'admin' ? 'Admin' : c.role === 'editor' ? 'Editor' : 'Nutzer'}</span></span><span class="c-plan">${planChip(c)}</span>
      <span class="cn">${esc(c.note || '')}${c.self ? ' <span class="muted">(du)</span>' : ''}</span>
      <span class="cs">${c.revoked ? '<span class="chip r">Gesperrt</span>' : c.hwid ? `<span class="chip g">Gebunden</span><small>PC ${esc(c.hwid)} · ${ago(c.last_seen || c.bound_at)}${c.rebinds ? ` · <span title="Neuinstallation erkannt: Windows wurde auf demselben PC neu installiert, der Key ist automatisch mitgezogen (${c.rebinds}×)">neu installiert</span>` : ''}</small>` : '<span class="chip w">Noch frei</span>'}</span>
      <span class="c-act">${c.self ? '' : `${c.hwid ? `<button class="btn sm" onclick="admAct('unbind','${esc(c.code)}')">PC lösen</button>` : ''}<button class="btn sm ${c.revoked ? '' : 'd'}" onclick="admAct('revoke','${esc(c.code)}',${c.revoked ? 1 : 0})">${c.revoked ? 'Entsperren' : 'Sperren'}</button>`}</span>
    </div>`).join('') : `<div class="lx-empty">${ico('users')}<b>Noch keine Codes</b><span>Erzeug oben den ersten Key</span></div>`;
  } else if (admTab === 'pending') {
    const r = await srv('GET', '/api/mods/pending'); if (r.error) return toast('Fehler', r.error, false);
    $('#admPendC').textContent = r.mods.length;
    $('#admPending').innerHTML = r.mods.length ? r.mods.map(m => `<div class="lcard"><div class="lthumb" style="background-image:url('${esc(m.img || bgUrl())}')"></div>
      <div class="lbody"><b>${esc(m.name)}</b><span class="lby">${esc((LIB_CATS.find(c => c[0] === m.cat) || [0, 'Sonstiges'])[1])} · ${bytes(m.size)} · von ${esc(m.author)}</span><p>${esc(m.desc || '')}</p>
      <code class="cc lurl" title="${esc(m.url)}">${esc(m.url)}</code>
      <div class="lact"><button class="btn sm d" onclick="admMod('delete','${esc(m.id)}')">${ico('trash')}Ablehnen</button><button class="btn p sm" onclick="admMod('approve','${esc(m.id)}')">${ico('check')}Freigeben</button></div></div></div>`).join('')
      : `<div class="lx-empty ok">${ico('check')}<b>Alles erledigt</b><span>Keine offenen Einreichungen</span></div>`;
  } else {
    const r = await srv('GET', '/api/mods'); if (r.error) return toast('Fehler', r.error, false);
    if ($('#admModCount')) $('#admModCount').textContent = r.mods.length;
    $('#admMods').innerHTML = r.mods.length ? r.mods.map(m => `<div class="mod adm-mod"><span class="am-th" style="background-image:url('${esc(m.img || picFor(m.id))}')"></span><div class="tx"><b>${esc(m.name)}</b><p>${esc((LIB_CATS.find(c => c[0] === m.cat) || [0, 'Sonstiges'])[1])} · ${bytes(m.size)}${m.author ? ' · von ' + esc(m.author) : ''}</p></div><button class="ib" title="Download-Link kopieren: ${esc(m.url)}" onclick="copyCmd('${esc(m.url)}')">${ico('link')}</button><button class="btn sm d" onclick="admMod('delete','${esc(m.id)}')">${ico('trash')}Löschen</button></div>`).join('') : `<div class="lx-empty">${ico('puzzle')}<b>Noch keine Server-Mods</b><span>Mods von Admins und freigegebene Einreichungen landen hier</span></div>`;
  }
}
/** Runtime of a key in words: "Lifetime", "30-Tage-Key · noch 12 Tage (bis 28.12.2026)", "abgelaufen am …". */
const daysName = d => d ? (d === 1 ? '1-Tag-Key' : `${d}-Tage-Key`) : 'Unbegrenzter Key';
function planText(p) {
  if (!p || !p.days) return 'Läuft nicht ab.';
  if (!p.exp) return `${daysName(p.days)}, die Laufzeit startet beim Einlösen.`;
  const ms = p.exp * 1000 - Date.now(), date = new Date(p.exp * 1000).toLocaleDateString('de-DE');
  if (ms <= 0) return `${daysName(p.days)}, abgelaufen am ${date}.`;
  const left = ms < 86400000 ? `noch ${Math.max(1, Math.round(ms / 3600000))} Std.` : `noch ${Math.round(ms / 86400000)} Tage`;
  return `${daysName(p.days)}, ${left} (bis ${date}).`;
}
function planChip(c) {
  if (!c.days) return '<span class="chip">Unbegrenzt</span>';
  const name = c.days === 1 ? '1 Tag' : c.days + ' Tage';
  if (!c.expires_at) return `<span class="chip">${name}</span>`;
  const ms = c.expires_at * 1000 - Date.now();
  return ms <= 0 ? `<span class="chip r">${name} · abgelaufen</span>` : `<span class="chip w">${name} · noch ${ms < 86400000 ? Math.max(1, Math.round(ms / 3600000)) + ' Std.' : Math.round(ms / 86400000) + ' T.'}</span>`;
}
async function admCreate() {
  const role = $('#admRole .on')?.dataset.v || 'user', days = +($('#admDays .on')?.dataset.v || 0);
  const count = Math.max(1, Math.min(50, parseInt($('#admNum').value) || 1));
  const r = await srv('POST', '/api/codes', { role, note: $('#admNote').value, days, count });
  if (r.error) return toast('Fehler', r.error, false);
  // a key server without runtimes (worker not updated yet) answers without 'days': the key would never expire
  if (days && !('days' in r)) toast('Achtung: Key läuft nicht ab', 'Dein Key-Server kennt noch keine Laufzeiten (Worker nicht aktualisiert). Dieser Key gilt unbegrenzt.', false);
  const codes = r.codes || [r.code];
  $('#admNote').value = ''; $('#admNum').value = 1;
  const kind = `${role === 'admin' ? 'Admin' : role === 'editor' ? 'Editor' : 'Nutzer'}-Key${codes.length > 1 ? 's' : ''} · ${days ? (days === 1 ? '1 Tag' : days + ' Tage') : 'unbegrenzt'}`;
  ADM_NEW = codes.join('\n');
  $('#admNewCode').classList.remove('hidden');
  $('#admNewCode').innerHTML = `<div><span class="ac-k">${ico('check')}${codes.length > 1 ? codes.length + ' neue ' : 'Neuer '}${kind}</span>${codes.map(c => `<code>${esc(c)}</code>`).join('')}</div><button class="btn" onclick="copyCmd(ADM_NEW)">${ico('copy')}${codes.length > 1 ? 'Alle kopieren' : 'Kopieren'}</button>`;
  admLoad();
}
let ADM_NEW = '';
document.addEventListener('click', e => { const b = e.target.closest('#admDays button'); if (b) $$('#admDays button').forEach(x => x.classList.toggle('on', x === b)); });
async function admAct(kind, code, undo) {
  if (kind === 'revoke' && !undo && !await confirmBox('Code sperren?', code + ' funktioniert danach nicht mehr.', { ok: 'Sperren', danger: true })) return;
  const r = await srv('POST', '/api/codes/' + kind, { code, undo: !!undo });
  if (r.error) return toast('Fehler', r.error, false);
  toast(kind === 'unbind' ? 'PC gelöst' : undo ? 'Code entsperrt' : 'Code gesperrt', kind === 'unbind' ? 'Der Code kann jetzt auf einem neuen PC eingelöst werden' : code);
  admLoad();
}
async function admMod(kind, id) {
  if (kind === 'delete' && !await confirmBox('Mod löschen?', 'Der Eintrag wird aus der Bibliothek entfernt.', { ok: 'Löschen', danger: true })) return;
  const r = await srv('POST', '/api/mods/' + kind, { id });
  if (r.error) return toast('Fehler', r.error, false);
  toast(kind === 'approve' ? 'Freigegeben' : 'Gelöscht', kind === 'approve' ? 'Die Mod ist jetzt für alle sichtbar' : '');
  admLoad(); admPendingCount(); libLoad().then(libRender);
}
window.admCreate = admCreate; window.admAct = admAct; window.admMod = admMod; window.admLoad = admLoad;

/* ======================= LICENSE GATE ======================= */
const LOCK = { resolve: null, busy: false };
const lkInputs = () => [0, 1, 2].map(i => $('#lkIn' + i));
function gate(reason) {
  return new Promise(res => {
    LOCK.resolve = res;
    const l = $('#lock'); l.classList.remove('out', 'ok'); l.classList.add('show');
    $('#lkCard').classList.remove('done', 'busy');
    $('#lkTitle').innerHTML = 'Willkommen bei <span class="grad">OrbitTweak</span>';
    $('#lkSub').textContent = reason || 'Gib deinen Lizenz-Key ein, um OrbitTweak freizuschalten.';
    $('#lkErr').textContent = ''; lkInputs().forEach(i => i.value = '');
    $('.lk-bg').style.backgroundImage = `url('${bgUrl()}')`;
    api('auth.info').then(i => $('#lkHwid').textContent = i.hwid || '…').catch(() => { });
    setTimeout(() => lkInputs()[0].focus(), 600);
  });
}
function lkFill(text) {
  const v = text.toUpperCase().replace(/[^A-Z0-9]/g, '').replace(/^ORB/, '').slice(0, 12);
  lkInputs().forEach((inp, i) => inp.value = v.slice(i * 4, i * 4 + 4));
  const next = lkInputs().find(x => x.value.length < 4); (next || lkInputs()[2]).focus();
}
document.addEventListener('input', e => {
  const i = lkInputs().indexOf(e.target); if (i < 0) return;
  const v = e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, '');
  if (v.length > 4 || /ORB/.test(v)) return lkFill(lkInputs().map(x => x.value).join(''));
  e.target.value = v; $('#lkKey').classList.remove('bad'); $('#lkErr').textContent = '';
  if (v.length === 4 && i < 2) lkInputs()[i + 1].focus();
});
document.addEventListener('paste', e => { if (lkInputs().includes(e.target)) { e.preventDefault(); lkFill(e.clipboardData.getData('text')); } });
document.addEventListener('keydown', e => {
  const i = lkInputs().indexOf(e.target); if (i < 0) return;
  if (e.key === 'Enter') lkSubmit();
  if (e.key === 'Backspace' && !e.target.value && i > 0) lkInputs()[i - 1].focus();
});
async function lkSubmit() {
  if (LOCK.busy) return;
  const parts = lkInputs().map(x => x.value);
  if (parts.some(p => p.length !== 4)) return lkFail('Bitte den kompletten Key eingeben (ORB-XXXX-XXXX-XXXX).');
  LOCK.busy = true; $('#lkCard').classList.add('busy'); $('#lkErr').textContent = '';
  const t0 = performance.now();
  let r; try { r = await api('auth.redeem', { code: 'ORB-' + parts.join('-') }); } catch (e) { r = { error: e.message }; }
  await sleep(Math.max(0, 900 - (performance.now() - t0)));   // let the scan animation breathe
  $('#lkCard').classList.remove('busy'); LOCK.busy = false;
  if (!r || r.error) return lkFail(r && r.error || 'Unbekannter Fehler');
  await lkSuccess(r.note, r.role, r.rebound);
}
function lkFail(msg) {
  const c = $('#lkCard'); c.classList.remove('shake'); void c.offsetWidth; c.classList.add('shake');
  $('#lkKey').classList.add('bad'); $('#lkErr').textContent = msg;
}
async function lkSuccess(note, role, rebound) {
  const c = $('#lkCard'); c.classList.add('done');
  $('#lkTitle').innerHTML = 'Freigeschaltet';
  $('#lkSub').textContent = (note ? `Willkommen, ${note}!` : 'Willkommen!') + (role === 'admin' ? ' Admin-Rechte aktiv.' : role === 'editor' ? ' Editor-Rechte aktiv.' : '') + (rebound ? ' Neuinstallation erkannt, dein Key läuft weiter.' : '');
  ACC.loggedIn = true; ACC.role = role;
  await sleep(1500);
  $('#lock').classList.add('out');
  await sleep(700);
  $('#lock').classList.remove('show', 'out');
  if (LOCK.resolve) { const r = LOCK.resolve; LOCK.resolve = null; r(); }
  accUI && accUI();
}
$('#lkGo') && ($('#lkGo').onclick = lkSubmit);

/** "AMD Ryzen 7 7800X3D 8-Core Processor" → "Ryzen 7 7800X3D", "Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz" → "Core i7-12700K". */
const cpuShort = s => String(s || '—').replace(/\((R|TM)\)/gi, '').replace(/\s*@.*$/, '').replace(/\b(\d+|Six|Eight|Quad)-Core\b/gi, '').replace(/\b(Processor|CPU|with Radeon Graphics)\b/gi, '').replace(/\b(AMD|Intel|Core)\b/gi, '').replace(/\bRyzen \d+ /i, 'Ryzen ').replace(/\s+/g, ' ').trim() || String(s);
/* ======================= BOOT ======================= */
async function boot() {
  const t0 = performance.now();
  initLook();
  // once faded out the splash leaves the page, otherwise its spinning rings would keep being animated unseen
  const hideSplash = () => { const sp = $('#splash'); if (!sp || sp.classList.contains('out')) return; sp.classList.add('out'); setTimeout(() => sp.style.display = 'none', 900); };
  setTimeout(hideSplash, 5000);
  $('#side').onclick = e => { const n = e.target.closest('.nav'); if (n) go(n.dataset.p); };
  document.addEventListener('mousedown', e => {
    if (e.button !== 0 || !e.target.closest('[data-drag]') || e.target.closest('button,.pill')) return;
    api(e.detail === 2 ? 'win.max' : 'win.drag');
  });
  document.addEventListener('contextmenu', e => { if (!e.target.closest('input,.cmd,.logv')) e.preventDefault(); });
  initLab();

  const d = await api('init');
  S.settings = d.settings; S.fivem = d.fivem;
  REST.away = d.focused === false; REST.game = !!d.game;
  applyTheme(S.settings.accent);
  restSync();
  moveInd(); enterPage($('#p-home')); if (document.fonts) document.fonts.ready.then(moveInd);
  $('#uname').textContent = d.user;
  $('#verTxt').textContent = 'v' + d.version; $('#verCur').textContent = 'v' + d.version;
  setTimeout(() => { if (!document.querySelector('.lock.show, .modal.show')) whatsNew(d.version); }, 2600);
  $('#adminTxt').textContent = d.admin ? 'Admin ✓' : 'Kein Admin';
  $('#mCpuName').textContent = cpuShort(d.sys.cpu); $('#mCpuName').title = d.sys.cpu;
  $('#mGpu').textContent = d.sys.gpu.replace(/^NVIDIA (GeForce )?/i, '').replace(/^AMD /i, '').replace(/^Intel\(R\) /i, '');
  $('#mGpu').title = d.sys.gpu;
  // a short name fills the value line, a long one (RTX 4070 SUPER) takes two smaller lines of the same height
  $('#mGpu').classList.toggle('one', $('#mGpu').textContent.length <= 10);
  $('#mOs').textContent = /nvidia/i.test(d.sys.gpu) ? 'NVIDIA GeForce' : /amd|radeon/i.test(d.sys.gpu) ? 'AMD Radeon' : /intel/i.test(d.sys.gpu) ? 'Intel' : `${d.sys.ramGb} GB RAM`;
  if (d.settings.lastBoost) { $('#lastBoost').textContent = 'Letzter Boost: ' + new Date(d.settings.lastBoost).toLocaleDateString('de-DE'); $('#lastBoost').classList.remove('hidden'); }
  ntIdle();
  $('#launchSub').textContent = d.fivem.found ? 'mit Pre-Launch-Boost' : 'FiveM nicht gefunden';
  if (S.settings.autoUpdate !== false) setTimeout(() => checkUpdate(false), 1500);   // updates work even without a key
  const lic = await api('auth.check').catch(() => ({ ok: false }));
  if (!lic.ok) {
    setTimeout(hideSplash, Math.max(0, 1100 - (performance.now() - t0)));
    await gate(lic.error && lic.error !== 'Nicht angemeldet' ? lic.error : null);
  } else ACC.note = lic.note;
  $('#lkGo').onclick = lkSubmit;
  feed(d.fivem.found ? 'FiveM erkannt: ' + d.fivem.path : 'FiveM nicht gefunden: Pfad im Launcher wählen');
  renderSettings(); renderLauncher(); initFx(d.settings); initMinimap(d.settings); initClip(d.settings); libLoad().then(accLoad); if (d.settings.bgVersion) { api('bg.clear').catch(() => { }); S.settings.bgVersion = 0; } applyBg(); initPad(d.settings);
  $('#snapTg').classList.toggle('on', !!d.settings.snapTap); wsGame();
  wasdLoad();
  await loadTweaks();
  refreshCache(); ping(); setInterval(ping, 8000); curServer();
  api('health').then(h => { S.health = h; $('#hsHealth').textContent = h.score + '%'; }).catch(() => { });
  setInterval(() => { if (S.settings.autoUpdate !== false) checkUpdate(false); }, 2 * 3600 * 1000);
  setTimeout(hideSplash, Math.max(0, 1300 - (performance.now() - t0)));
  if (S.settings.firstRun) setTimeout(() => wizard(0), 1500);
}

/* ======================= MOCK BACKEND (browser preview) ======================= */
const Mock = {
  settings: { accent: 'inferno', autoInstall: false, restoreBeforeBoost: true, purgeStandby: true, autoPriority: true, autoRamClean: false, autoUpdate: true, firstRun: false, lastBoost: null, xhairAck: false, xhair: { on: false, onlyGame: false, key: 0x79, offsetX: 0, offsetY: 0, json: null }, display: { on: false, onlyGame: true, vibrance: 0, brightness: 0, contrast: 0, gamma: 100, key: 0 } },
  tweaks: [
    ['gpu', 'fivem', 'Hochleistungs-GPU erzwingen', 'FiveM läuft immer auf der dedizierten Grafikkarte statt der iGPU.', 'monitor', null, 1],
    ['fso', 'fivem', 'Vollbild-Optimierung für FiveM aus', 'Echter Exclusive-Fullscreen, weniger Input-Lag.', 'max', null, 1],
    ['prio', 'fivem', 'Auto-Priorität „Hoch“', 'GTA-Prozess bekommt automatisch hohe CPU-Priorität.', 'gauge', 'LIVE', 1],
    ['autoram', 'fivem', 'Auto-RAM-Clean im Spiel', 'Leert alle 5 Minuten Standby-Liste & Working-Sets.', 'ram', 'LIVE', 0],
    ['timer', 'fivem', 'Timer-Auflösung 0,5 ms', 'Stabilere Frametimes, solange OrbitTweak läuft.', 'timer', 'LIVE', 1],
    ['ult', 'system', 'Höchstleistungs-Energieplan', 'Kein CPU-Parking, kein Heruntertakten.', 'zap', null, 1],
    ['gm', 'system', 'Spielmodus', 'Windows priorisiert das Spiel.', 'gamepad', null, 1],
    ['dvr', 'system', 'Xbox Game DVR aus', 'Keine Hintergrundaufnahme.', 'videooff', null, 1],
    ['hags', 'system', 'Hardware-GPU-Planung', 'Weniger Latenz, stabilere Frametimes.', 'monitor', 'NEUSTART', 1],
    ['mmcss', 'system', 'Spiele-Priorität (MMCSS)', 'Mehr CPU- und GPU-Zeit für Spiele.', 'cpu', null, 1],
    ['nagle', 'network', 'Nagle-Algorithmus aus', 'Pakete werden sofort gesendet.', 'wifi', null, 1],
    ['thr', 'network', 'Netzwerk-Drosselung aus', 'Keine Multimedia-Paketdrosselung.', 'activity', null, 1],
    ['kbd', 'input', 'Tastatur-Verzögerung 0', 'Minimale Wiederholverzögerung, maximale Repeat-Rate.', 'keyboard', null, 1],
    ['queue', 'input', 'Input-Puffer optimieren', 'Kleinere Maus- und Tastatur-Warteschlange.', 'mouse', 'NEUSTART', 1],
    ['accel', 'input', 'Mausbeschleunigung aus', '1:1-Gefühl beim Zielen.', 'target', null, 1],
    ['usb', 'input', 'USB-Energiesparen aus', 'Keine Input-Aussetzer durch Stromsparen.', 'usb', null, 1],
  ].map(([id, cat, name, desc, icon, badge, rec], i) => ({ id, cat, name, desc, icon, badge, rec: !!rec, applied: i % 3 === 0 })),
  async call(cmd, a) {
    await sleep(100); const M = this;
    switch (cmd) {
      case 'init': setInterval(() => onEvent('stats', { cpu: Math.round(18 + Math.random() * 30), ram: 54, ramUsed: 17.3, ramTotal: 32, fm: { running: true, game: true, build: 'b3095', prio: 'Hoch', gameRam: 5.6e9 } }), 1000);
        return { user: 'Max', admin: true, version: '2.2.0', sys: { cpu: 'AMD Ryzen 7 7800X3D 8-Core Processor', gpu: 'NVIDIA GeForce RTX 4070 SUPER', os: 'Windows 11 Pro', ramGb: 32 }, fivem: { found: true, path: 'C:\\Users\\Max\\AppData\\Local\\FiveM' }, settings: M.settings };
      case 'tweaks.list': return M.tweaks;
      case 'tweak.set': { const t = M.tweaks.find(x => x.id === a.id); t.applied = a.on; return { applied: a.on }; }
      case 'clip.recycle': return { deleted: (a.rels || []).length, failed: 0 };
      case 'clip.share': await sleep(900); return { ok: true, name: 'Kill 2026-11-14 20-11-57 (10 MB).mp4', size: 9.4e6, shrunk: true, clipboard: true };
      case 'wasd.flash': return null;
      case 'clip.kills': return { today: 4, all: 11, busy: false };
      case 'clip.highlight': for (let i = 1; i <= 8; i++) { onEvent('highlight', { pct: i * 12, text: '4 Clips werden zusammengefügt…' }); await sleep(250); } return { file: 'Highlights 2026-11-14 21-40-00.mp4', clips: 4 };
      case 'wasd.get': case 'wasd.lag': case 'wasd.set': case 'wasd.chop':
        if (cmd === 'wasd.lag') M.lag = a.on;
        if (cmd === 'wasd.set') { M.wasd = a.size; M.wasdPending = true; }
        if (cmd === 'wasd.chop') M.chop = { on: a.on, gap: a.gap, every: a.every, key: a.key || 0, keyOk: true };
        return { size: M.wasd || 0, pending: !!M.wasdPending, lag: !!M.lag, delay: 200, repeat: 15, chop: M.chop || { on: false, gap: 30, every: 90, key: 0, keyOk: true } };
      case 'boost': for (let i = 0; i <= 10; i++) { onEvent('boost', { pct: i * 10, text: 'Schritt ' + i }); await sleep(220); } M.tweaks.forEach(t => t.rec && (t.applied = true)); return { freed: 1.4e9, restart: true, applied: 9 };
      case 'xhair.set': return { keyOk: true };
      case 'clip': return { ok: false };
      case 'net.test': await sleep(900); return { ok: true, avg: 23, min: 19, max: 31, jitter: 2.4, loss: 0, samples: Array.from({ length: 20 }, () => 19 + Math.round(Math.random() * 12)) };
      case 'minimap.set': return { ok: true, client: { w: 1920, h: 1080 } };
      case 'minimap.preview': return null;
      case 'minimap.pick': case 'minimap.info': {
        const c = document.createElement('canvas'); c.width = c.height = 512; const x = c.getContext('2d');
        const g = x.createRadialGradient(256, 256, 20, 256, 256, 250); g.addColorStop(0, '#FF7A3D'); g.addColorStop(1, '#3A1466');
        x.fillStyle = g; x.beginPath(); x.arc(256, 256, 240, 0, 7); x.fill(); x.lineWidth = 12; x.strokeStyle = '#fff'; x.stroke();
        x.fillStyle = '#fff'; x.font = '900 200px Inter, sans-serif'; x.textAlign = 'center'; x.fillText('OT', 256, 326);
        return { path: 'C:\\Users\\Max\\Pictures\\clan-logo.png', name: 'clan-logo.png', w: 1024, h: 1024, preview: c.toDataURL(), detail: c.toDataURL() };
      }
      case 'minimap.status': return { status: { found: true, installed: !!M.mm, on: true, size: 14.2e6, date: new Date().toISOString() }, cfg: { path: '', mode: 'tile', repeat: 24, res: 2048, fit: 'stretch', dim: 0 } };
      case 'minimap.build': for (let i = 1; i <= 6; i++) { onEvent('mmbuild', { pct: i * 15, text: `Kachel ${i} von 6…` }); await sleep(260); } M.mm = true; return { size: 14.2e6 };
      case 'minimap.remove': M.mm = false; return {};
      case 'clip.status': return { recorder: CL.recorder, running: true, buffered: 34, w: 1920, h: 1080, error: null, game: true, supported: true, found: 'tr', armed: true, detect: CL.detect, markAt: null, markImg: null, kills: 0, fights: 0, net: CL.net !== false };
      case 'clip.save': if (CL.recorder === 'orbit') setTimeout(() => onEvent('clip', { recorder: 'orbit', file: 'Clip 2026-11-14 16-02-11.mp4' }), 500); return { ok: true };
      case 'clip.cfg': return { keyOk: true, combo: 'Alt+F10' };
      case 'clip.marktest': {
        await sleep(1800);
        const c = document.createElement('canvas'); c.width = 480; c.height = 240; const g = c.getContext('2d'), gr = g.createLinearGradient(0, 0, 0, 240);
        gr.addColorStop(0, '#6d8299'); gr.addColorStop(.55, '#4a4f50'); gr.addColorStop(1, '#262521'); g.fillStyle = gr; g.fillRect(0, 0, 480, 240);
        g.strokeStyle = '#fff'; g.lineWidth = 2; g.beginPath(); g.moveTo(232, 120); g.lineTo(248, 120); g.moveTo(240, 112); g.lineTo(240, 128); g.stroke();
        g.font = '700 22px sans-serif'; g.textAlign = 'left'; g.fillStyle = '#ff4d4d'; g.fillText('KILLED', 150, 172); g.fillStyle = '#fff'; g.fillText('Tony_Ricci', 242, 172);
        return { ok: true, game: true, front: true, img: c.toDataURL(), kind: 'kill', word: 'KILLED', text: 'KILLED Tony_Ricci', how: 'nur helle und farbige Schrift', lines: [{ text: 'KILLED Tony_Ricci', hit: 'kill' }, { text: '+1 STREAK', hit: null }] };
      }
      case 'clip.kftest': return { ok: true, game: true, img: null, lines: [{ text: ((a && a.name) || 'Max_Power') + ' [Pistol] Tony_Ricci', hit: 'kill' }, { text: 'Lena_V [AP] Jimmy', hit: null }, { text: 'Ricci [Rifle] ' + ((a && a.name) || 'Max_Power'), hit: 'death' }] };
      case 'clip.open': case 'clip.gamebar': return { ok: true };
      case 'clip.delete': return { ok: true };
      case 'clip.list': {
        const at = h => new Date(Date.now() - h * 36e5), nm = (k, d) => `${k} ${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}-${String(d.getMinutes()).padStart(2, '0')}-${String(d.getSeconds()).padStart(2, '0')}`;
        const own = [['Kill', .1, 25], ['2 Kills', .6, 31], ['Montage', 1.2, 89], ['Gefecht', 2, 25], ['Kill', 26, 25], ['Clip', 27, 25]].map(([k, h, dur]) => { const d = at(h), n = nm(k, d); return { name: n, rel: 'OrbitTweak/' + n + '.mp4', folder: 'OrbitTweak', size: dur * 7.2e5, date: d.toISOString(), ext: '.mp4', dur }; });
        return own.concat(['Grand Theft Auto V 2026.11.13 - 21.14.08.02', 'Gangwar Clutch'].map((n, i) => ({ name: n, rel: 'Grand Theft Auto V/' + n + '.mp4', folder: i ? 'Captures' : 'Grand Theft Auto V', size: 48e6 + i * 9e6, date: at(50 + i * 24).toISOString(), ext: '.mp4' })));
      }
      case 'clip.montage': for (let i = 1; i <= 8; i++) { onEvent('highlight', { pct: i * 12, text: `Kill ${Math.min(i, 10)} von 10 wird geschnitten…` }); await sleep(250); } return { ok: true, file: 'Montage 2026-11-14 21-40-00.mp4', clips: 10, bpm: MO.music ? 128 : 0 };
      case 'clip.montageMusic': return a.clear ? { music: null } : { music: 'Phonk Drift.mp3' };
      case 'mods.list': return { found: true, gta: true, items: [
        { id: 'a1', name: 'Gun Sounds V2', on: true, size: 212e6, cat: 'sound', places: ['citizen'], paths: ['FiveM.app\\citizen\\platform\\audio\\sfx\\WEAPONS_PLAYER.rpf', 'FiveM.app\\citizen\\platform\\audio\\sfx\\RESIDENT.rpf'], pure: 0 },
        { id: 'b2', name: 'round_minimap_dark', on: true, size: 3.4e6, cat: 'minimap', places: ['mods'], paths: ['FiveM.app\\mods\\round_minimap_dark.rpf'], pure: 0 },
        { id: 'c3', name: 'WEAPONS_PLAYER', on: true, size: 96e6, cat: 'sound', places: ['mods'], paths: ['FiveM.app\\mods\\WEAPONS_PLAYER.rpf'], pure: 0, broken: 'Wirkt nicht: FiveM ignoriert diese Datei im mods-Ordner. Sound-Packs gehören in den citizen-Ordner.', fix: true },
        { id: 'd4', name: 'Glock Camo', on: false, size: 8.1e6, cat: 'skin', places: ['mods'], paths: ['FiveM.app\\mods\\ot-glock-camo-3f2a.rpf'], pure: 0 },
        { id: 'e5', name: 'Siren Pack', on: true, size: 54e6, cat: 'sound', places: ['gta'], paths: ['GTA V\\x64\\audio\\sfx\\RESIDENT.rpf'], pure: 1 }] };
      case 'mods.repair': await sleep(500); return { count: 2, notes: ['Sound-Pack erkannt (WEAPONS_PLAYER). FiveM lädt Sounds nicht aus dem mods-Ordner – OrbitTweak legt sie in den citizen-Ordner. Wirkt auf Servern ohne Pure-Mode.'], mods: [{ id: 'c3', name: 'WEAPONS_PLAYER', cat: 'sound', places: ['citizen'] }], off: [] };
      case 'pad.set': return { keyOk: true };
      case 'pad.watch': return { active: true, state: { c: true, t: 'ps', name: 'PlayStation 4 DualShock', b: { a: true, rb: true, up: true }, lx: .5, ly: -.3, rx: 0, ry: .6, lt: .4, rt: 1 } };
      case 'mods.browse': return null;
      case 'mods.install': return { count: 1 };
      case 'mods.lib': return { installed: [] };
      case 'auth.info': return { hwid: '3fa9c1e07b2d', role: 'admin', loggedIn: true };
      case 'auth.check': return /lock/.test(location.search) ? { ok: false } : { ok: true, role: 'admin', note: 'Ky3ls' };
      case 'auth.redeem': await sleep(400); return a.code.endsWith('0000') ? { error: 'Code ungültig oder gesperrt' } : { ok: true, role: 'admin', note: 'Ky3ls', rebound: a.code.endsWith('1111') };
      case 'auth.logout': return { ok: true };
      case 'srv': return MOCKSRV(a);
      case 'reshade.cfg.get': return { found: true, keyOverlay: 36, keyEffects: 145, keyScreenshot: 44, keyNext: 0, showFps: false, perf: true };
      case 'reshade.cfg.set': case 'reshade.shots': return {};
      case 'discord.state': {   // Discord status card: as if Discord runs and the game is on NovaCity
        const s = M.settings, on = s.discordOn !== false, id = s.discordAppId || '';
        return { status: !on ? 'off' : !id ? 'noid' : 'on', user: 'Max', error: null, appId: id, builtIn: false,
          preview: on && id ? { details: s.discordServer !== false ? 'NovaCity | Roleplay [GER]' : 'FiveM', state: s.discordKills !== false ? 'Im Spiel · 3 Kills' : 'Im Spiel', since: new Date(Date.now() - 42 * 60e3).toISOString() } : null };
      }
      case 'server.current': return { found: true, connected: true, key: 'cfx:abc123', url: 'cfx.re/join/abc123', fav: false, server: { name: '^1Nova^7City ^3| Roleplay [GER]', desc: 'Die lebendigste Stadt Deutschlands · Whitelist · Eigene Wirtschaft', allTags: ['roleplay', 'deutsch', 'economy', 'whitelist', 'jobs', 'gangs', 'custom cars'], locale: 'DE', players: 187, max: 256, ping: 22, build: '3095', onesync: true, resources: 412, owner: 'NovaTeam', discord: 'https://discord.gg/novacity', website: 'https://novacity.de', connect: 'cfx.re/join/abc123', online: true } };
      case 'mods.downloads': return [{ name: 'Killsound pack v2.zip', size: 212e6, date: new Date().toISOString(), rpfs: 4, cat: 'sound', what: 'Sound-Pack: WEAPONS_PLAYER, RESIDENT' }, { name: 'satellite_minimap_v2.zip', size: 18e6, date: new Date().toISOString(), rpfs: 1, cat: 'minimap', what: 'FiveM-Paket' }, { name: 'KillFX_RedPulse.rar', size: 2.4e6, date: new Date(Date.now() - 864e5).toISOString(), rpfs: -1, cat: 'kill' }];
      case 'mods.installDl': await sleep(600);
        if (/Killsound/.test(a.name) && a.variant == null) return { choose: ['FiveM Version', 'Singleplayer (OpenIV)'] };
        return /Killsound/.test(a.name) ? { count: 2, notes: ['Sound-Pack erkannt (WEAPONS_PLAYER, RESIDENT). FiveM lädt Sounds nicht aus dem mods-Ordner – OrbitTweak legt sie in den citizen-Ordner. Wirkt auf Servern ohne Pure-Mode.'], off: ['Gun Sounds V2'], mods: [{ id: 'f6', name: 'Killsound pack v2', cat: 'sound', places: ['citizen'] }] } : { count: 1, notes: [], off: [], mods: [{ id: 'g7', name: a.name.replace(/\.\w+$/, ''), cat: a.cat, places: ['mods'] }] };
      case 'reshade.status': return { found: true, installed: true, version: '6.8.0', preset: 'vivid', presets: [{ id: 'vivid', name: 'Lebendig', desc: 'Kräftige Farben, knackig scharf' }, { id: 'pvp', name: 'Klar (PvP)', desc: 'Maximale Sichtbarkeit, scharfe Kanten' }, { id: 'cine', name: 'Cinematic', desc: 'Filmlook mit Vignette' }, { id: 'night', name: 'Nachtsicht', desc: 'Dunkle Ecken sichtbar' }, { id: 'techni', name: 'Technicolor', desc: 'Satte Retro-Farben' }] };
      case 'reshade.install': for (let i = 1; i <= 5; i++) { onEvent('reshade', { pct: i * 20, text: 'Schritt ' + i }); await sleep(250); } return {};
      case 'reshade.preset': case 'reshade.uninstall': return {};
      case 'thumb': return { url: null };
      case 'clip.thumb': return { url: picFor(a.rel) };   // preview only: a game photo stands in for the frame of the clip
      case 'bg.list': return [];
      case 'bg.set': case 'bg.pick': case 'bg.clear': return { v: 0 };
      case 'downloads.open': return null;
      case 'mods.toggle': case 'mods.delete': case 'mods.folder': return {};
      case 'f8.send': await sleep(500); return { sent: true };
      case 'f8.values': return { cl_drawfps: 'true' };
      case 'gw.info': return { found: true, mode: 'windowed', w: 1600, h: 900 };
      case 'gw.borderless': case 'gw.windowed': return { ok: true };
      case 'display.get': return { cfg: M.settings.display, nvidia: true, unlocked: false, gammaOk: null };
      case 'display.set': M.settings.display = a.cfg; return { active: a.cfg.on, gammaOk: true, vibOk: true, keyOk: true };
      case 'display.preview': return { active: false };
      case 'display.unlock': return { ok: true };
      case 'ping': return { ms: 18 + Math.round(Math.random() * 6), jitter: 2 };
      case 'fivem.cacheSize': return { found: true, size: 3.2e9 };
      case 'servers.list': return [
        { code: 'k4', name: '^8Nova^7City ^1| ^7Roleplay ^3[GER]', desc: 'Die lebendigste Stadt Deutschlands', players: 187, max: 256, online: true, ping: 22, locale: 'DE', tags: ['roleplay', 'economy'] },
        { code: 'r2', name: '^4Blue^7Line ^5PvP ^7Arena', desc: 'Gangwar · FFA · Ranked', players: 64, max: 128, online: true, ping: 31, locale: 'DE', tags: ['pvp'] },
        { code: 'aa', name: '^2Green^7Valley RP', desc: 'Whitelist-Server', players: 0, max: 64, online: false, ping: null, locale: 'EN', tags: ['whitelist'] }];
      case 'cfx.status': return [{ name: 'Server-Liste', ok: true, ms: 41 }, { name: 'Cfx.re Web', ok: true, ms: 57 }, { name: 'Content-CDN', ok: true, ms: 23 }, { name: 'Cfx.re Forum', ok: true, ms: 64 }];
      case 'gfx.get': return { found: true, path: 'C:\\Users\\Max\\Documents\\Rockstar Games\\GTA V\\settings.xml', values: { ...PRESETS.bal.v } };
      case 'ini.get': return { channel: 'production' };
      case 'clean.scan': return [['fm', 'FiveM Cache', 'cache, server-cache, server-cache-priv', 'gamepad', 3.2e9], ['logs', 'FiveM Logs & Crashes', 'Alte Logs und Crashdumps', 'file', 2.1e8], ['temp', 'Temporäre Dateien', 'Benutzer- und System-Temp', 'folder', 1.6e9], ['shader', 'Shader-Cache', 'DirectX / NVIDIA / AMD', 'monitor', 7.4e8], ['wu', 'Windows-Update Reste', 'Bereits installierte Pakete', 'download', 9.1e8], ['bin', 'Papierkorb', 'Endgültig leeren', 'trash', 2.4e8]].map(([id, name, desc, icon, size], i) => ({ id, name, desc, icon, size, sel: i < 5 }));
      case 'health': return { score: 74, items: [{ name: 'FiveM installiert', value: 'C:\\Users\\Max\\AppData\\Local\\FiveM', st: 'ok' }, { name: 'Arbeitsspeicher', value: '32 GB, perfekt für FiveM', st: 'ok' }, { name: 'Energieplan', value: 'Ausbalanciert, CPU taktet herunter', st: 'warn', fix: 'tweak:ult' }, { name: 'Xbox Game DVR', value: 'Aktiv, kostet FPS', st: 'bad', fix: 'tweak:dvr' }, { name: 'GPU-Treiber', value: 'NVIDIA 566.36 · 4 Monate alt', st: 'ok' }, { name: 'FiveM-Cache', value: '3.2 GB, leeren empfohlen', st: 'warn', fix: 'cache' }] };
      case 'logs': return { lines: ['[ 1203] Loading game build 3095', '[ 2210] Warning: texture budget exceeded', '[ 4501] Connecting to 127.0.0.1:30120', '[ 9012] SCRIPT ERROR: @core/server.lua:12 attempt to index nil', '[12001] Session started'], crashes: [] };
      case 'update.check': return { configured: true, current: '2.2.0', latest: '2.3.0', available: new URLSearchParams(location.search).has('upd'), date: '12.11.2026', notes: ['Neuer Look mit weniger Effekten', 'Server-Favoriten laden schneller', 'Fix: Launcher-Pfad wird gespeichert'] };
      default: return { ok: true, freed: 3.4e8, count: 0, applied: true };
    }
  }
};

boot();


function MOCKSRV(a) {
  const p = a.path;
  if (p === '/api/me') return { role: 'admin' };
  if (p === '/api/mods' && a.method === 'GET') return { mods: [{ id: 'm-demo1', type: 'file', name: 'Red Flash Kill-Effekt', cat: 'kill', url: 'https://example.com/redflash.zip', size: 2.1e6, author: 'Community', desc: 'Kurzer roter Blitz bei jedem Kill.', tags: ['pvp', 'kill'], popular: 90, added: '2026-11-14', status: 'live' }] };
  if (p === '/api/mods/pending') return { mods: [{ id: 'm-p1', name: 'Dark Satellite Minimap', cat: 'minimap', url: 'https://example.com/darksat.zip', size: 18e6, author: 'MapMaker', desc: 'Satellitenkarte in dunkel.', status: 'pending' }] };
  if (p === '/api/codes' && a.method === 'GET') return { codes: [{ code: 'ORB-7K2D-QX9M-4HTP', role: 'admin', note: 'Ky3ls', hwid: '3fa9c1e07b', last_seen: Date.now() / 1000 - 30, self: true }, { code: 'ORB-M8VN-2CKE-W7RJ', role: 'editor', note: 'Max', hwid: '9b1e44a2c0', last_seen: Date.now() / 1000 - 7200, rebinds: 1 }, { code: 'ORB-T4QH-8ZPA-3NXD', role: 'editor', note: 'Lena', hwid: null }] };
  if (p === '/api/codes' && a.method === 'POST') { const n = a.body.count || 1, codes = Array.from({ length: n }, (_, i) => 'ORB-N3WC-0D3X-YZ' + String(10 + i).slice(-2)); return { code: codes[0], codes, role: a.body.role, days: a.body.days || null }; }
  return { ok: true, status: 'pending' };
}

/* ======================= MOD TABS (2.16) ======================= */
function modTab(k) {
  document.documentElement.dataset.mtab = k;
  $$('#modTabs .tab').forEach(b => b.classList.toggle('on', b.dataset.mt === k));
  $$('#p-mods .mtab').forEach(p => p.classList.toggle('hidden', p.dataset.mtp !== k));
  const pane = $(`#p-mods [data-mtp="${k}"]`); if (pane) enterPage($('#p-mods'));
  if (k === 'mm') { mmbOpen(); mmoUI(); }
  else if (MMO.prev) mmoPreview(false);   // the on-screen preview of the minimap picture ends with its tab
}
document.addEventListener('click', e => { const b = e.target.closest('[data-mt]'); if (b) modTab(b.dataset.mt); });
window.modTab = modTab;

/* ======================= GENERIC PAGE TABS (2.17) ======================= */
function pageTab(group, k) {
  $$(`[data-tabs="${group}"] .tab`).forEach(b => b.classList.toggle('on', b.dataset.tab === k));
  $$(`[data-pane^="${group}:"]`).forEach(p => p.classList.toggle('hidden', p.dataset.pane !== group + ':' + k));
}
document.addEventListener('click', e => {
  const b = e.target.closest('[data-tabs] .tab'); if (b) return pageTab(b.closest('[data-tabs]').dataset.tabs, b.dataset.tab);
  const g = e.target.closest('[data-gotab]'); if (g) { const [grp, k] = g.dataset.gotab.split(':'); pageTab(grp, k); }
});

/* ======================= SETTINGS PROFILE (2.18) ======================= */
function spProfile() {
  if (!$('#spTitle')) return;
  const r = (typeof ACC !== 'undefined' && ACC.loggedIn) ? ACC.role : (S.settings.authRole || null);
  const rn = r === 'admin' ? 'Admin' : r === 'editor' ? 'Editor' : r ? 'Nutzer' : '–';
  $('#spTitle').textContent = r ? 'Lizenz aktiv' : 'Lizenz';
  $('#spSub').textContent = r ? 'Freigeschaltet und an diesen PC gebunden' : 'Status wird geladen…';
  if ($('#spRole')) $('#spRole').textContent = rn;
  $('#spAv').className = 'sp-av' + (r === 'admin' ? ' adm' : '');
  $('#spVer').textContent = $('#verCur')?.textContent || '–';
  // from the result of the last check, not from the wording of its label ("neueste" also contains "neue")
  $('#spUpd').textContent = S.updAvail === true ? 'Update da' : S.updAvail === false ? 'Aktuell' : S.settings.autoUpdate ? 'Automatisch' : 'Manuell';
}

/* ======================= KILL FEED (2.20) ======================= */
let kfNameT = 0;
document.addEventListener('input', e => {
  // kill words of the server's kill message (sent a moment after typing stops)
  if (e.target.id === 'clWords') { CL.words = e.target.value; clearTimeout(kfNameT); kfNameT = setTimeout(clipPush, 900); return; }
  if (e.target.id !== 'clName' && e.target.id !== 'kcName') return;
  CL.name = e.target.value; clearTimeout(kfNameT); kfNameT = setTimeout(() => { clipPush(); clipUI(); }, 500);
});
// leaving the field shows the tidied list (an empty field goes back to the usual words)
document.addEventListener('focusout', e => { if (e.target.id === 'clWords') { clearTimeout(kfNameT); clipPush(); } });
// OpenRouter key for the AI check: saved when the field is left, then emptied (the key is never shown again)
document.addEventListener('change', async e => {
  if (e.target.id !== 'clAiKey') return;
  const key = e.target.value.trim(); if (!key) return;
  const r = await api('clip.aikey', { key }).catch(() => null);
  if (r && r.ok) { e.target.value = ''; toast('Key gespeichert', 'Die KI-Prüfung ist bereit'); clipStatus(); }
  else toast('Key nicht gespeichert', (r && r.error) || 'Bitte nochmal versuchen', false);
});
async function kfTest() {
  const out = $('#clKfOut'), btn = $('#clKfTest');
  out.classList.remove('hidden'); out.innerHTML = `<div class="sub" style="font-size:12px">${ico('refresh', 'i spin')} Lese Kill-Feed…</div>`;
  btn.disabled = true;
  let r; try { r = await api('clip.kftest', { name: CL.name, area: CL.area }); } catch (err) { r = { ok: false, error: err.message }; }
  btn.disabled = false;
  if (!r || !r.ok) { out.innerHTML = `<div class="kf-err">${ico('alert')}${esc((r && r.error) || 'Test fehlgeschlagen')}</div>`; return; }
  const lines = r.lines || [];
  const hits = lines.filter(l => l.hit).length;
  out.innerHTML = `${r.game ? '' : `<div class="kf-err">${ico('alert')}FiveM ist nicht im Vordergrund. Starte FiveM, wechsle ins Spiel und teste dann mit der Clip-Taste oder hier nochmal.</div>`}
    ${CL.area === 'auto' && r.area ? `<p class="sub" style="font-size:12px;margin-bottom:8px">${hits ? `Kill-Feed gefunden: <b>${KF_NAMES[r.area] || r.area}</b>` : `Noch kein Kill mit deinem Namen zu sehen. Gezeigt wird die Stelle ${KF_NAMES[r.area] || r.area}, im Spiel sucht OrbitTweak alle Stellen ab.`}</p>` : ''}
    <div class="kf-cols"><div class="kf-img">${r.img ? `<img src="${r.img}" alt="">` : ''}<span>Das sieht OrbitTweak</span></div>
    <div class="kf-lines"><b>Erkannter Text</b>${lines.length ? lines.map(l => `<div class="kf-l ${l.hit || ''}">${l.hit === 'kill' ? '<em>Kill</em>' : l.hit === 'death' ? '<em>Tod</em>' : ''}<span>${esc(l.text)}</span></div>`).join('') : '<div class="sub" style="font-size:12px">Kein Text gefunden. Stimmt die Position? Steht gerade etwas im Kill-Feed?</div>'}
    ${lines.length && !hits ? `<p class="sub" style="font-size:11.5px;margin-top:8px">Dein Name „${esc(CL.name)}“ wurde nicht gefunden. Erst nach einem Kill steht er im Feed.</p>` : ''}</div></div>`;
}
window.kfTest = kfTest;
// the same for the crosshair: the host gives FiveM a few seconds to come to the front, then reads the strip around the
// crosshair once, all three ways (picture as it is, white text, bright and coloured text)
async function mkTest() {
  const out = $('#clMkOut'), btn = $('#clMkTest');
  let left = 6;
  const wait = () => `<div class="mk-wait">${ico('refresh', 'i spin')}<span>Wechsle jetzt ins Spiel. OrbitTweak liest gleich einmal die Bildmitte${left > 0 ? ` (noch ${left} s)` : ''}…</span></div>`;
  out.classList.remove('hidden'); out.innerHTML = wait();
  const tick = setInterval(() => { if (--left >= 0) out.innerHTML = wait(); }, 1000);
  btn.disabled = true;
  let r; try { r = await api('clip.marktest', { wait: 6 }); } catch (err) { r = { ok: false, error: err.message }; }
  clearInterval(tick); btn.disabled = false;
  if (!r || !r.ok) { out.innerHTML = `<div class="kf-err">${ico('alert')}${esc((r && r.error) || 'Test fehlgeschlagen')}</div>`; return; }
  const lines = r.lines || [];
  const verdict = r.kind === 'kill' ? `<div class="mk-v kill">${ico('check')}<span>Kill-Meldung erkannt: <b>${esc(r.word)}</b> in „${esc(r.text)}“ · gelesen über ${esc(r.how)}</span></div>`
    : r.kind === 'death' ? `<div class="mk-v death">${ico('alert')}<span>Dein Tod erkannt: „${esc(r.text)}“. Zählt nicht als Kill.</span></div>`
    : `<div class="mk-v">${ico('eye')}<span>Gerade keine Kill-Meldung in der Bildmitte. Ohne frischen Kill ist das normal, wichtig ist: Das Bild zeigt die Mitte deines Spiels.</span></div>`;
  out.innerHTML = `${!r.game ? `<div class="kf-err">${ico('alert')}FiveM läuft nicht. Das Bild zeigt die Mitte deines Bildschirms.</div>` : !r.front ? `<div class="kf-err">${ico('alert')}FiveM war nicht im Vordergrund. Das Bild zeigt, was gerade in der Bildmitte war.</div>` : ''}${verdict}
    <div class="kf-cols"><div class="kf-img">${r.img ? `<img src="${r.img}" alt="">` : ''}<span>Das sieht OrbitTweak</span></div>
    <div class="kf-lines"><b>Erkannter Text</b>${lines.length ? lines.map(l => `<div class="kf-l ${l.hit || ''}">${l.hit === 'kill' ? '<em>Kill</em>' : l.hit === 'death' ? '<em>Tod</em>' : ''}<span>${esc(l.text)}</span></div>`).join('') : '<div class="sub mk-none">Kein Text gefunden.</div>'}
    ${lines.length && !r.kind ? '<p class="sub mk-none">Steht bei deinen Kills ein anderes Wort da, trag es oben bei den Kill-Wörtern ein.</p>' : ''}</div></div>`;
}

/* ======================= CUSTOM MINIMAP FROM A PICTURE (2.21) ======================= */
// GTA's map is 2 × 3 tiles of 4500 game units each. The radar shows roughly 360 units of it while on foot.
const MM_W = 9000, MM_H = 13500, MM_VIEW = 360;
const MMB = { path: '', name: '', w: 0, h: 0, img: null, det: null, mode: 'tile', repeat: 24, res: 2048, fit: 'stretch', dim: 0, busy: false, st: null, loaded: false };
function mmbSet(i) {
  Object.assign(MMB, { path: i.path, name: i.name, w: i.w, h: i.h, img: null, det: null });
  const im = new Image(); im.onload = () => { MMB.img = im; mmbUI(); }; im.src = i.preview;
  if (i.detail) { const d = new Image(); d.onload = () => { MMB.det = d; mmbDraw(); }; d.src = i.detail; }
}
async function mmbOpen() {
  let r = null; try { r = await api('minimap.status'); } catch { }
  MMB.st = r ? r.status : null;
  if (r && r.cfg && !MMB.loaded) {
    MMB.loaded = true; const c = r.cfg;
    Object.assign(MMB, { mode: c.mode === 'map' ? 'map' : 'tile', repeat: Math.min(60, Math.max(6, c.repeat || 24)), res: c.res >= 3072 ? 3072 : 2048, fit: ['stretch', 'cover', 'contain'].includes(c.fit) ? c.fit : 'stretch', dim: c.dim || 0 });
    if (c.path) { const i = await api('minimap.info', { path: c.path }).catch(() => null); if (i && !i.error) mmbSet(i); }
  }
  mmbUI();
}
function mmbUI() {
  if (!$('#mmbPick')) return;
  const has = !!MMB.path, st = MMB.st || {};
  $('#mmbName').textContent = has ? MMB.name : 'Bild auswählen';
  $('#mmbInfo').textContent = has ? `${MMB.w} × ${MMB.h} Pixel` : 'PNG, JPG oder WebP';
  const th = $('#mmbTh'); th.style.backgroundImage = has && MMB.img ? `url('${MMB.img.src}')` : ''; th.classList.toggle('has', has && !!MMB.img);
  $$('#mmbMode button').forEach(b => b.classList.toggle('on', b.dataset.v === MMB.mode));
  $('#mmbTile').classList.toggle('hidden', MMB.mode !== 'tile'); $('#mmbMap').classList.toggle('hidden', MMB.mode !== 'map');
  const rng = (el, v) => { el.value = v; el.style.setProperty('--p', (v - el.min) / (el.max - el.min) * 100 + '%'); };
  rng($('#mmbRepeat'), 66 - MMB.repeat); $('#mmbRepeatV').textContent = Math.round(MM_W / MMB.repeat) + ' m';
  rng($('#mmbDim'), MMB.dim); $('#mmbDimV').textContent = MMB.dim + '%';
  $$('#mmbFit button').forEach(b => b.classList.toggle('on', b.dataset.v === MMB.fit));
  $$('#mmbRes button').forEach(b => b.classList.toggle('on', +b.dataset.v === MMB.res));
  const chip = $('#mmbChip'); chip.className = 'chip' + (st.installed && st.on ? ' g' : ''); chip.textContent = st.installed ? (st.on ? 'Installiert · aktiv' : 'Installiert · ausgeschaltet') : 'Nicht installiert';
  if ($('#mtMm')) $('#mtMm').textContent = MMO.on && MMO.img ? 'Overlay aktiv' : st.installed ? (st.on ? 'Aktiv' : 'Ausgeschaltet') : 'Aus Bild';
  $('#mmbRemove').classList.toggle('hidden', !st.installed);
  const b = $('#mmbBuild'); b.disabled = !has || MMB.busy; $('#mmbNeed')?.classList.toggle('hidden', has);
  b.querySelector('span').textContent = MMB.busy ? 'Wird erstellt…' : st.installed ? 'Minimap neu erstellen' : 'Minimap erstellen & installieren';
  const small = MMB.mode === 'map' && has && MMB.w < 3000;
  $('#mmbHint').innerHTML = !has ? 'Wähl links ein Bild aus, dann siehst du hier sofort, wie dein Radar damit aussieht.'
    : MMB.mode === 'tile' ? `Dein Bild wiederholt sich über die ganze Karte, ein Bild ist etwa <b>${Math.round(MM_W / MMB.repeat)} m</b> breit. Das Radar zeigt zu Fuß rund ${MM_VIEW} m, im Auto mehr.`
    : `Dein Bild wird über die ganze Karte gelegt (Norden oben). Das Radar zeigt davon nur einen kleinen Ausschnitt.${small ? ` <b style="color:var(--warn)">Dein Bild ist mit ${MMB.w} Pixeln Breite dafür klein und wirkt im Radar unscharf.</b> Besser sind 4096 × 6144 Pixel oder „Bild wiederholen“.` : ''}`;
  mmbDraw();
}
/** Draws the part "win" of the map (game units) into a canvas the way the mod will look. */
function mmbPaint(cv, win) {
  const x = cv.getContext('2d'), W = cv.width, H = cv.height;
  x.setTransform(1, 0, 0, 1, 0, 0); x.filter = 'none'; x.imageSmoothingQuality = 'high';
  x.fillStyle = '#35607A'; x.fillRect(0, 0, W, H);                      // the sea shows through where the picture is see-through
  const im = MMB.img; if (!im) return false;
  const sx = W / win.w, sy = H / win.h;
  x.filter = MMB.dim ? `brightness(${100 - MMB.dim}%)` : 'none';
  if (MMB.mode === 'tile') {
    const pw = MM_W / MMB.repeat * sx, ph = pw * im.height / im.width;
    const t = document.createElement('canvas'); t.width = Math.max(1, Math.round(pw)); t.height = Math.max(1, Math.round(ph));
    const tx = t.getContext('2d'); tx.imageSmoothingQuality = 'high'; tx.drawImage(im, 0, 0, t.width, t.height);
    const p = x.createPattern(t, 'repeat');
    p.setTransform(new DOMMatrix().translate(-((win.x * sx) % t.width), -((win.y * sy) % t.height)));
    x.fillStyle = p; x.fillRect(0, 0, W, H);
  } else {
    let r = { x: 0, y: 0, w: MM_W, h: MM_H };
    if (MMB.fit !== 'stretch') {
      const s = MMB.fit === 'cover' ? Math.max(MM_W / MMB.w, MM_H / MMB.h) : Math.min(MM_W / MMB.w, MM_H / MMB.h);
      r = { w: MMB.w * s, h: MMB.h * s }; r.x = (MM_W - r.w) / 2; r.y = (MM_H - r.h) / 2;
    }
    x.save();
    x.beginPath(); x.rect(-win.x * sx, -win.y * sy, MM_W * sx, MM_H * sy); x.clip();
    const at = (ux, uy, uw, uh, img) => x.drawImage(img, (ux - win.x) * sx, (uy - win.y) * sy, uw * sx, uh * sy);
    at(r.x, r.y, r.w, r.h, im);
    // the middle 12 % in full resolution, limited to what the mod's texture can hold
    if (MMB.det && win.w < MM_W / 4) {
      const texel = Math.min(MMB.w, 2 * MMB.res) / MMB.w;                // 1 = nothing is lost
      let d = MMB.det;
      if (texel < 1) { const c = document.createElement('canvas'); c.width = Math.max(1, Math.round(d.width * texel)); c.height = Math.max(1, Math.round(d.height * texel)); c.getContext('2d').drawImage(d, 0, 0, c.width, c.height); d = c; }
      at(r.x + r.w * 0.44, r.y + r.h * 0.44, r.w * 0.12, r.h * 0.12, d);
    }
    x.restore();
  }
  x.filter = 'none';
  return true;
}
function mmbDraw() {
  const radar = $('#mmbRadar'), world = $('#mmbWorld'); if (!radar) return;
  // radar: the middle of the map, like standing in the city (repeating pictures: centred on one picture)
  const vw = MM_VIEW, vh = MM_VIEW * radar.height / radar.width;
  let cx = MM_W / 2, cy = MM_H / 2;
  if (MMB.mode === 'tile' && MMB.img) { const pw = MM_W / MMB.repeat, ph = pw * MMB.img.height / MMB.img.width; cx = (Math.floor(cx / pw) + .5) * pw; cy = (Math.floor(cy / ph) + .5) * ph; }
  const has = mmbPaint(radar, { x: cx - vw / 2, y: cy - vh / 2, w: vw, h: vh });
  let x = radar.getContext('2d'); const W = radar.width, H = radar.height;
  if (!has) { x.fillStyle = 'rgba(255,255,255,.6)'; x.font = '600 26px Inter, sans-serif'; x.textAlign = 'center'; x.fillText('Noch kein Bild gewählt', W / 2, H * .26); }
  // player arrow, a few blips and the health/armour bars, so it reads as the GTA radar
  const blip = (bx, by, col, r = 11) => { x.beginPath(); x.arc(bx, by, r, 0, 7); x.fillStyle = col; x.fill(); x.lineWidth = 3; x.strokeStyle = 'rgba(0,0,0,.75)'; x.stroke(); };
  blip(W * .72, H * .3, '#E04B4B'); blip(W * .2, H * .62, '#5BA7F0'); blip(W * .58, H * .74, '#F2C94C', 9);
  x.save(); x.translate(W / 2, H / 2); x.beginPath(); x.moveTo(0, -20); x.lineTo(14, 16); x.lineTo(0, 8); x.lineTo(-14, 16); x.closePath();
  x.fillStyle = '#fff'; x.fill(); x.lineWidth = 3; x.strokeStyle = 'rgba(0,0,0,.8)'; x.stroke(); x.restore();
  x.fillStyle = 'rgba(0,0,0,.6)'; x.fillRect(0, H - 22, W, 22);
  x.fillStyle = '#5BBF57'; x.fillRect(6, H - 16, W * .5 - 30, 10); x.fillStyle = 'rgba(91,191,87,.3)'; x.fillRect(W * .5 - 24, H - 16, 15, 10);
  x.fillStyle = '#4A9BD8'; x.fillRect(W * .5 - 3, H - 16, W * .25 - 6, 10); x.fillStyle = '#D9C24A'; x.fillRect(W * .75 - 3, H - 16, W * .25 - 3, 10);
  // whole map, with a marker for the size of the radar view
  mmbPaint(world, { x: 0, y: 0, w: MM_W, h: MM_H });
  x = world.getContext('2d');
  if (!has) { x.fillStyle = 'rgba(255,255,255,.6)'; x.font = '600 24px Inter, sans-serif'; x.textAlign = 'center'; x.fillText('Kein Bild', world.width / 2, world.height / 2 + 8); return; }
  const k = world.width / MM_W;
  x.strokeStyle = '#fff'; x.lineWidth = 2; x.setLineDash([4, 3]);
  x.strokeRect(world.width / 2 - Math.max(6, vw * k / 2), world.height / 2 - Math.max(4, vh * k / 2), Math.max(12, vw * k), Math.max(8, vh * k)); x.setLineDash([]);
}
function mmbProgress(d) { const p = $('#mmbProg'); if (!p) return; p.classList.remove('hidden'); $('#mmbBar').style.width = d.pct + '%'; $('#mmbProgTxt').textContent = d.text; }
async function mmbBuild() {
  if (!MMB.path || MMB.busy) return;
  if (!S.settings.modsAck) return modInstall('__dl');
  MMB.busy = true; mmbUI(); mmbProgress({ pct: 1, text: 'Starte…' });
  const r = await api('minimap.build', { cfg: { path: MMB.path, mode: MMB.mode, repeat: MMB.repeat, res: MMB.res, fit: MMB.fit, dim: MMB.dim } }).catch(e => ({ error: e.message }));
  MMB.busy = false; $('#mmbProg').classList.add('hidden');
  if (r.error) toast('Minimap nicht erstellt', r.error, false);
  else { passed('Eigene Minimap installiert', `${bytes(r.size)} · aktiv ab dem nächsten FiveM-Start`); modList(); }
  mmbOpen();
}
document.addEventListener('click', async e => {
  if (!e.target.closest('[data-mtp="mm"]')) return;
  if (e.target.closest('#mmbPick')) {
    const i = await api('minimap.pick').catch(er => ({ error: er.message }));
    if (i.cancelled) return;
    if (i.error) return toast('Bild nicht geladen', i.error, false);
    mmbSet(i); mmbUI(); return;
  }
  const m = e.target.closest('#mmbMode button'); if (m) { MMB.mode = m.dataset.v; mmbUI(); return; }
  const f = e.target.closest('#mmbFit button'); if (f) { MMB.fit = f.dataset.v; mmbUI(); return; }
  const q = e.target.closest('#mmbRes button'); if (q) { MMB.res = +q.dataset.v; mmbUI(); return; }
  if (e.target.closest('#mmbBuild')) return mmbBuild();
  if (e.target.closest('#mmbRemove')) {
    if (!await confirmBox('Eigene Minimap entfernen?', 'Im Spiel ist danach wieder die normale Karte zu sehen.', { ok: 'Entfernen', danger: true })) return;
    const r = await api('minimap.remove').catch(er => ({ error: er.message }));
    if (r.error) toast('Nicht entfernt', r.error, false); else toast('Eigene Minimap entfernt', 'Ab dem nächsten FiveM-Start');
    modList(); mmbOpen();
  }
});
document.addEventListener('input', e => {
  if (e.target.id === 'mmbRepeat') { MMB.repeat = 66 - +e.target.value; mmbUI(); }
  else if (e.target.id === 'mmbDim') { MMB.dim = +e.target.value; mmbUI(); }
});

/* ======================= v1 polish: motion ======================= */
// a ring of light from the spot a button is pressed
document.addEventListener('pointerdown', e => {
  const b = e.target.closest('.btn, .launch, .tab, .seg button, .nav');
  if (!b || b.disabled) return;
  const r = b.getBoundingClientRect(), d = Math.max(r.width, r.height) * 2.2, s = document.createElement('span');
  s.className = 'rp'; s.style.cssText = `width:${d}px;height:${d}px;left:${e.clientX - r.left - d / 2}px;top:${e.clientY - r.top - d / 2}px`;
  b.appendChild(s); setTimeout(() => s.remove(), 650);
});
// cards below the visible part of a page come in when they are scrolled to
// (own class name: plain 'in' is the style of text fields and squeezed every such card to the height of an input box)
const rvIO = new IntersectionObserver(es => es.forEach(en => { if (en.isIntersecting) { en.target.classList.add('rv-in'); rvIO.unobserve(en.target); } }), { threshold: .08 });
function rvScan() {
  const pg = document.querySelector('.page.show'); if (!pg) return;
  pg.querySelectorAll('.card:not(.rv)').forEach(c => { if (c.getBoundingClientRect().top > innerHeight - 40) { c.classList.add('rv'); rvIO.observe(c); } });
}
{ const go0 = go; go = function (p) { const r = go0.apply(this, arguments); setTimeout(rvScan, 60); return r; }; }
setTimeout(rvScan, 800);

/* ======================= MINIGAMES: AIM TRAINING ======================= */
const MG_MODES = {
  grid: ['Raster', 'Drei große Ziele gleichzeitig. Triff so viele wie möglich.', 3, 34, 0],
  flick: ['Präzision', 'Ein kleines Ziel. Jeder Fehlklick kostet Punkte.', 1, 15, 0],
  speed: ['Tempo', 'Die Ziele verschwinden nach kurzer Zeit wieder. Sei schneller.', 2, 26, 950],
};
const MG = { mode: 'grid', on: false, t0: 0, end: 0, targets: [], hits: 0, shots: 0, score: 0, lastHit: 0, sum: 0, pops: [], raf: 0 };
function mgBest() { try { return JSON.parse(localStorage.getItem('orbitAim') || '{}'); } catch { return {}; } }
function mgIdle() {
  const [n, d] = MG_MODES[MG.mode];
  $('#mgTitle').textContent = n; $('#mgDesc').textContent = d; $('#mgOver').classList.remove('done');
  $$('#mgMode button').forEach(b => b.classList.toggle('on', b.dataset.mg === MG.mode));
  const b = mgBest()[MG.mode]; $('#mgBest').textContent = b ? b.score : 0; $('#mgBestNote').textContent = b ? `${b.acc} % Trefferquote · ${b.date}` : 'Noch kein Durchgang in diesem Modus';
}
function mgSpawn(cv) {
  const r = MG_MODES[MG.mode][3] * devicePixelRatio, pad = r + 14 * devicePixelRatio;
  for (let i = 0; i < 40; i++) {
    const x = pad + Math.random() * (cv.width - pad * 2), y = pad + 50 * devicePixelRatio + Math.random() * (cv.height - pad * 2 - 50 * devicePixelRatio);
    if (MG.targets.every(t => Math.hypot(t.x - x, t.y - y) > r * 2.6)) return MG.targets.push({ x, y, r, born: performance.now() });
  }
}
function mgDraw() {
  const cv = $('#mgCv'), g = cv.getContext('2d'), now = performance.now();
  const st = getComputedStyle(document.documentElement), a = st.getPropertyValue('--a').trim(), b = st.getPropertyValue('--b').trim();
  g.clearRect(0, 0, cv.width, cv.height);
  const life = MG_MODES[MG.mode][4];
  if (life) MG.targets = MG.targets.filter(t => now - t.born < life);
  while (MG.on && MG.targets.length < MG_MODES[MG.mode][2]) mgSpawn(cv);
  const dp = devicePixelRatio;
  for (const t of MG.targets) {
    const k = Math.min(1, (now - t.born) / 140), fade = life ? 1 - Math.max(0, (now - t.born - life * .6) / (life * .4)) : 1, r = t.r * (.5 + .5 * k);
    g.globalAlpha = Math.max(.15, fade);
    // a solid disc with light from above, a soft shadow below, a fine ring and a bright centre
    g.shadowColor = 'rgba(0,0,0,.55)'; g.shadowBlur = 18 * dp; g.shadowOffsetY = 6 * dp;
    g.fillStyle = a; g.beginPath(); g.arc(t.x, t.y, r, 0, 7); g.fill();
    g.shadowColor = 'transparent'; g.shadowBlur = 0; g.shadowOffsetY = 0;
    const lt = g.createLinearGradient(0, t.y - r, 0, t.y + r); lt.addColorStop(0, 'rgba(255,255,255,.3)'); lt.addColorStop(.55, 'rgba(255,255,255,0)'); lt.addColorStop(1, 'rgba(0,0,0,.12)');
    g.fillStyle = lt; g.beginPath(); g.arc(t.x, t.y, r, 0, 7); g.fill();
    g.strokeStyle = 'rgba(255,255,255,.5)'; g.lineWidth = 1.25 * dp; g.beginPath(); g.arc(t.x, t.y, r * .6, 0, 7); g.stroke();
    g.strokeStyle = 'rgba(255,255,255,.16)'; g.lineWidth = 1 * dp; g.beginPath(); g.arc(t.x, t.y, r + 4 * dp, 0, 7); g.stroke();
    g.fillStyle = 'rgba(255,255,255,.95)'; g.beginPath(); g.arc(t.x, t.y, r * .18, 0, 7); g.fill();
  }
  g.globalAlpha = 1;
  MG.pops = MG.pops.filter(p => now - p.t < 420);
  for (const p of MG.pops) {
    const k = (now - p.t) / 420;
    g.strokeStyle = p.hit ? a : 'rgba(242,139,139,.9)'; g.globalAlpha = 1 - k; g.lineWidth = 1.5 * dp;
    g.beginPath(); g.arc(p.x, p.y, p.r * (1 + k * 1.2), 0, 7); g.stroke();
  }
  g.globalAlpha = 1;
  if (MG.on) {
    const left = Math.max(0, (MG.end - now) / 1000), el = $('#mgTime');
    el.textContent = nf1.format(left); el.classList.toggle('low', left < 5);
    const bar = $('#mgBar'); if (bar) bar.style.transform = `scaleX(${(left / 30).toFixed(4)})`;
    if (left <= 0) return mgStop();
  }
  if (MG.on || MG.pops.length) MG.raf = requestAnimationFrame(mgDraw);
}
function mgStats() {
  $('#mgScore').textContent = MG.score;
  $('#mgAcc').innerHTML = MG.shots ? Math.round(MG.hits / MG.shots * 100) + '<small>%</small>' : '–';
  $('#mgAvg').innerHTML = MG.hits ? Math.round(MG.sum / MG.hits) + '<small>ms</small>' : '–';
}
function mgStart() {
  const cv = $('#mgCv'), r = cv.getBoundingClientRect();
  cv.width = Math.round(r.width * devicePixelRatio); cv.height = Math.round(r.height * devicePixelRatio);
  Object.assign(MG, { on: true, targets: [], hits: 0, shots: 0, score: 0, sum: 0, pops: [], lastHit: performance.now(), end: performance.now() + 30000 });
  $('#mgOver').classList.add('off'); mgStats(); cancelAnimationFrame(MG.raf); mgDraw();
}
function mgStop() {
  MG.on = false; MG.targets = [];
  const acc = MG.shots ? Math.round(MG.hits / MG.shots * 100) : 0, all = mgBest(), old = all[MG.mode];
  const best = !old || MG.score > old.score;
  if (best && MG.score > 0) { all[MG.mode] = { score: MG.score, acc, date: new Date().toLocaleDateString('de-DE') }; try { localStorage.setItem('orbitAim', JSON.stringify(all)); } catch { } }
  mgIdle();
  $('#mgBig').textContent = MG.score; $('#mgOver').classList.add('done');
  $('#mgTitle').textContent = best && MG.score > 0 ? 'Neuer Bestwert' : MG.score === 1 ? 'Punkt' : 'Punkte';
  $('#mgDesc').textContent = `${MG.hits} Treffer bei ${MG.shots} Klicks, ${acc} % Trefferquote${MG.hits ? `, ${Math.round(MG.sum / MG.hits)} ms pro Treffer` : ''}.`;
  $('#mgStart').innerHTML = `${ico('refresh')}Nochmal`;
  $('#mgOver').classList.remove('off'); $('#mgTime').textContent = '30,0'; $('#mgTime').classList.remove('low');
}
document.addEventListener('pointerdown', e => {
  if (e.target.id !== 'mgCv' || !MG.on) return;
  const cv = e.target, r = cv.getBoundingClientRect(), x = (e.clientX - r.left) * devicePixelRatio, y = (e.clientY - r.top) * devicePixelRatio, now = performance.now();
  MG.shots++;
  const i = MG.targets.findIndex(t => Math.hypot(t.x - x, t.y - y) <= t.r);
  if (i >= 0) {
    const t = MG.targets.splice(i, 1)[0];
    MG.hits++; MG.sum += now - MG.lastHit; MG.lastHit = now;
    MG.score += MG.mode === 'flick' ? 3 : MG.mode === 'speed' ? 2 : 1;
    MG.pops.push({ x: t.x, y: t.y, r: t.r, t: now, hit: true });
  } else {
    if (MG.mode === 'flick') MG.score = Math.max(0, MG.score - 1);
    MG.pops.push({ x, y, r: 10 * devicePixelRatio, t: now, hit: false });
  }
  mgStats();
});
document.addEventListener('click', e => {
  const m = e.target.closest('#mgMode button');
  if (m && !MG.on) { MG.mode = m.dataset.mg; $('#mgStart').innerHTML = `${ico('play')}Starten`; mgIdle(); }
  if (e.target.closest('#mgStart')) mgStart();
});
{ const go1 = go; go = function (p) { if (curPage === 'games' && p !== 'games' && MG.on) { MG.on = false; MG.targets = []; $('#mgOver').classList.remove('off'); } const r = go1.apply(this, arguments); if (p === 'games') mgIdle(); return r; }; window.go = go; }

/* ======================= v1 polish, round three ======================= */
// live curves for RAM and ping on the start page, small live bars in the title bar
const LIVE = { ram: [], ping: [] };
function liveCurve(hist, line, fill, max) {
  if (hist.length < 2) return;
  const n = hist.length, pts = hist.map((v, i) => [i * 200 / (n - 1), 58 - Math.min(1, v / max) * 52]), d = smoothPath(pts);
  $(line).setAttribute('d', d); $(fill).setAttribute('d', d + ' L200,60 L0,60 Z');
}
setInterval(() => {
  const cpu = parseFloat($('#tbCpu').textContent), ram = parseFloat($('#tbRam').textContent), ping = parseFloat($('#tbPing').textContent);
  if (!isNaN(cpu)) { $('#tbCpuP').style.setProperty('--v', cpu); $('#tbCpuP').classList.toggle('hot', cpu >= 90); }
  // (the title bar shows a placeholder until the first stats arrive: no samples before that; a new curve starts flat)
  if (!isNaN(ram) && S.cpuHist.length) { $('#tbRamP').style.setProperty('--v', ram); $('#tbRamP').classList.toggle('hot', ram >= 90); if (!LIVE.ram.length) LIVE.ram = Array(39).fill(ram); LIVE.ram.push(ram); if (LIVE.ram.length > 40) LIVE.ram.shift(); }
  if (!isNaN(ping)) { if (!LIVE.ping.length) LIVE.ping = Array(39).fill(ping); LIVE.ping.push(ping); if (LIVE.ping.length > 40) LIVE.ping.shift(); }
  if (curPage === 'home') { liveCurve(LIVE.ram, '#sparkRL', '#sparkRF', 100); liveCurve(LIVE.ping, '#sparkPL', '#sparkPF', Math.max(60, ...LIVE.ping) * 1.2); }
}, 1000);

/* ======================= side bar: folding groups, entrance, page direction ======================= */
{
  const side = document.querySelector('.side-nav');
  let folded = []; try { folded = JSON.parse(localStorage.getItem('orbitNavFold') || '[]'); } catch { }
  const groupOf = h => { const r = []; for (let n = h.nextElementSibling; n && !n.classList.contains('nav-h'); n = n.nextElementSibling) if (n.classList.contains('nav')) r.push(n); return r; };
  const fold = (h, on, save = true) => {
    h.classList.toggle('cl', on);
    // the entry of the page that is open stays visible, so the marker never points at nothing
    groupOf(h).forEach(n => n.classList.toggle('gone', on && !n.classList.contains('active')));
    if (save) { folded = [...side.querySelectorAll('.nav-h.cl')].map(x => x.textContent.trim()); try { localStorage.setItem('orbitNavFold', JSON.stringify(folded)); } catch { } }
    setTimeout(moveInd, 370);
  };
  side.querySelectorAll('.nav-h').forEach(h => { if (folded.includes(h.textContent.trim())) fold(h, true, false); });
  side.addEventListener('click', e => { const h = e.target.closest('.nav-h'); if (h) fold(h, !h.classList.contains('cl')); });
  side.querySelectorAll('.nav').forEach((n, i) => { n.style.setProperty('--n', i); n.classList.add('nav-in'); setTimeout(() => n.classList.remove('nav-in'), 1400); });
  // a page comes in from below when it is further down in the menu, from above otherwise; folded groups show their open page
  const order = [...document.querySelectorAll('.nav[data-p]')].map(n => n.dataset.p);
  const go2 = go; go = function (p) {
    const from = order.indexOf(NAV_OF[curPage] || curPage), to = order.indexOf(NAV_OF[p] || p);
    document.documentElement.style.setProperty('--dir', to >= from ? 1 : -1);
    const r = go2.apply(this, arguments);
    side.querySelectorAll('.nav-h.cl').forEach(h => groupOf(h).forEach(n => n.classList.toggle('gone', !n.classList.contains('active'))));
    setTimeout(moveInd, 370);
    return r;
  }; window.go = go;
}

/* ======================= CLIP EDITOR: cut a part out of a clip ======================= */
const CUT = { rel: '', from: 0, to: 0, dur: 0, busy: false };
const cutFmt = t => { const m = Math.floor(t / 60), s = t - m * 60; return `${m}:${s.toFixed(1).padStart(4, '0').replace('.', ',')}`; };
function cutUI() {
  const bar = $('#cutBar'); if (!bar) return;
  const a = CUT.from / CUT.dur * 100, b = CUT.to / CUT.dur * 100;
  $('#cutSel').style.left = a + '%'; $('#cutSel').style.width = (b - a) + '%';
  $('#cutA').style.left = a + '%'; $('#cutB').style.left = b + '%';
  $('#cutInfo').innerHTML = `<b>${cutFmt(CUT.from)}</b> bis <b>${cutFmt(CUT.to)}</b> · ${(CUT.to - CUT.from).toFixed(1).replace('.', ',')} s von ${CUT.dur.toFixed(1).replace('.', ',')} s`;
}
window.cutOpen = rel => {
  const v = $('#mbox video'); if (!v || !isFinite(v.duration)) return toast('Clip lädt noch', 'Einen Moment, dann nochmal', false);
  Object.assign(CUT, { rel, dur: v.duration, from: 0, to: v.duration, busy: false });
  v.removeAttribute('controls'); v.pause();
  $('#mbox .act').outerHTML = `
    <div class="cut">
      <div class="cut-bar" id="cutBar"><i class="cut-sel" id="cutSel"></i><i class="cut-pos" id="cutPos"></i><b class="cut-h" id="cutA"></b><b class="cut-h" id="cutB"></b></div>
      <div class="cut-row"><button class="btn sm" id="cutPlay">${ico('play')}Ausschnitt abspielen</button><span class="cut-info" id="cutInfo"></span><span class="sp"></span>
        <button class="btn sm gh" data-cutset="from">Anfang hier</button><button class="btn sm gh" data-cutset="to">Ende hier</button></div>
      <p class="cut-hint">Zieh die beiden Griffe oder klick in die Leiste, um an eine Stelle zu springen. Das Original bleibt, wie es ist.</p>
    </div>
    <div class="act"><button class="btn gh" onclick="closeModal()">Abbrechen</button><span class="sp"></span><button class="btn" id="cutShare">${ico('copy')}Schnitt für Discord kopieren</button><button class="btn p" id="cutSave">${ico('check')}Als neuen Clip speichern</button></div>`;
  v.ontimeupdate = () => { const p = $('#cutPos'); if (p) p.style.left = v.currentTime / CUT.dur * 100 + '%'; if (CUT.playing && v.currentTime >= CUT.to) { v.pause(); CUT.playing = false; } };
  cutUI();
};
document.addEventListener('pointerdown', e => {
  const bar = e.target.closest('#cutBar'); if (!bar) return;
  const v = $('#mbox video'), r = bar.getBoundingClientRect(), at = x => Math.min(1, Math.max(0, (x - r.left) / r.width)) * CUT.dur;
  const h = e.target.closest('.cut-h');
  if (!h) { v.currentTime = at(e.clientX); return; }
  const which = h.id === 'cutA' ? 'from' : 'to';
  const move = ev => {
    let t = at(ev.clientX);
    if (which === 'from') t = Math.min(t, CUT.to - 0.5); else t = Math.max(t, CUT.from + 0.5);
    CUT[which] = Math.max(0, Math.min(CUT.dur, t)); v.currentTime = CUT[which]; cutUI();
  };
  const up = () => { removeEventListener('pointermove', move); removeEventListener('pointerup', up); };
  addEventListener('pointermove', move); addEventListener('pointerup', up); e.preventDefault();
});
document.addEventListener('click', async e => {
  const v = $('#mbox video');
  const set = e.target.closest('[data-cutset]');
  if (set && v) {
    const t = v.currentTime;
    if (set.dataset.cutset === 'from') CUT.from = Math.min(t, CUT.to - 0.5); else CUT.to = Math.max(t, CUT.from + 0.5);
    CUT.from = Math.max(0, CUT.from); CUT.to = Math.min(CUT.dur, CUT.to); cutUI(); return;
  }
  if (e.target.closest('#cutPlay') && v) { v.currentTime = CUT.from; CUT.playing = true; v.play(); return; }
  const save = e.target.closest('#cutSave'), share = e.target.closest('#cutShare');
  if ((save || share) && !CUT.busy) {
    CUT.busy = true; const b = save || share, was = b.innerHTML; b.disabled = true; b.innerHTML = `${ico('refresh', 'i spin')}Schneide…`;
    if (v) v.pause();
    const r = await api('clip.cut', { rel: CUT.rel, from: +CUT.from.toFixed(2), to: +CUT.to.toFixed(2), share: !!share }).catch(er => ({ error: er.message }));
    CUT.busy = false; b.disabled = false; b.innerHTML = was;
    if (r.error) return toast('Nicht geschnitten', r.error, false);
    closeModal(); $('#mbox').style.width = '';
    if (r.share) toast(r.clipboard ? 'Schnitt kopiert' : 'Schnitt fertig', `${r.name} · ${bytes(r.size)}${r.clipboard ? ' · in Discord mit Strg+V einfügen' : ''}`);
    else { passed('Clip geschnitten', `${r.name} · ${bytes(r.size)}`); clipList(); }
  }
});

/* ======================= WHAT'S NEW: shown once after an update ======================= */
const CHANGES = {
  '2.25.0': [
    ['sparkle', 'Überall aufgeräumt', 'Jede Seite überarbeitet: ruhigere Buttons, saubere Abstände, klare Leerzustände, Dialoge mit eindeutigen Knöpfen und durchgehend deutsche Texte.'],
    ['film', 'Montage im Takt', 'Mit Musik landet jeder Kill auf einem Beat. Titel, Intro und Kill-Zähler stellst du direkt beim Video ein.'],
    ['crosshair', 'Kill-Erkennung testen', '„Erkennung testen“ gibt es jetzt auch fürs Fadenkreuz. Farbige Kill-Texte werden erkannt, ein Tod nach dem Kill landet als „Kill + Tod“ im Clip-Namen.'],
    ['shield', 'Key nach Neuinstallation', 'Installierst du Windows neu, zieht dein Key automatisch mit, ohne dass ein Admin ihn lösen muss. Auf einem anderen PC bleibt er gesperrt.'],
  ],
  '2.24.1': [
    ['film', 'Montage neu geschnitten', 'Etwa 5 Sekunden pro Kill statt 9: flüssige Zeitlupe, Zoom und Aufblitzen genau beim Kill, weiche Übergänge, Kill-Zähler im Killfeed-Stil, Intro und Outro. Ultrawide wird zugeschnitten statt mit Balken.'],
    ['zap', 'Montage startet wieder', 'Mit dem aktuellen ffmpeg 9 brach die Montage bei neuen Nutzern sofort ab. OrbitTweak lädt jetzt eine feste, geprüfte ffmpeg-Version.'],
    ['crosshair', 'Kill-Erkennung genauer', 'Wörter wie KILLER, Skilled oder FILLED zählen nicht mehr als Kill, „You have been killed“ zählt als Tod. Mehrere Kills kurz hintereinander werden einzeln erkannt, KI-geprüfte Clips enden direkt nach dem Gefecht.'],
  ],
  '2.24.0': [
    ['puzzle', 'Sound-Packs funktionieren', 'Mods landen jetzt dort, wo FiveM sie wirklich lädt: Sound-Packs im citizen-Ordner, Skins und Effekte als echtes Mod-Paket. Alte, wirkungslose Installationen repariert „Meine Mods“ mit einem Klick.'],
    ['sparkle', 'Discord-Status', 'Dein Discord-Profil zeigt, dass du FiveM mit OrbitTweak spielst: Server, Spielzeit und Kills, alles einzeln abschaltbar.'],
    ['film', 'Clipper neu aufgebaut', 'Status oben, deine Clips als Raster mit Länge und Kill-Abzeichen, ein Video-Baustein für Highlights und Montage, Kill-Erkennung nach Methode sortiert.'],
  ],
  '2.23.0': [
    ['gauge', 'Viel sparsamer', 'Die App zeichnet nur noch, was sich bewegt: kein Weichzeichner hinter jeder Karte, keine Dauer-Animationen. Im Spiel, im Hintergrund und minimiert ruht sie fast ganz.'],
    ['gamepad', 'Controller-Erkennung entlastet', 'Ohne Controller wird nur noch alle zwei Sekunden nachgesehen statt 160-mal pro Sekunde.'],
    ['sparkle', 'Anime-Theme', 'Abendhimmel über der Stadt mit Kirschblüten, runde Überschriften, Rasterpunkte und Fokuslinien.'],
    ['keyboard', 'Input-Lab aufgeräumt', 'Snap Tap steht vorn, bei jeder Einstellung steht ehrlich, ob sie im Spiel wirkt. Der Tester zeigt den Stillstand beim Richtungswechsel.'],
    ['sun', 'Winter-Theme überarbeitet', 'Nordlichter, Raureif auf den Karten, Eiszapfen am Banner, Schnee in drei Ebenen. Effekte unter Einstellungen → Design abschaltbar.'],
  ],
  '2.22.0': [
    ['zap', 'Montage mit Effekten', 'Deine neuesten Kills automatisch zu einem Video geschnitten: Titelkarte, Kill-Zähler, Blitz bei jedem Schnitt, auf Wunsch mit Musik.'],
    ['film', 'Clip-Editor', 'Clip öffnen, „Schneiden“, Anfang und Ende setzen, als neuen Clip speichern oder klein für Discord kopieren.'],
    ['crosshair', 'KI-Prüfung für Kills', 'Wo am Fadenkreuz kein Kill erkannt wird, schaut ein KI-Modell auf das Gefecht. Tode werden nicht mehr geclippt.'],
    ['download', 'Packs aus Discord', 'Sound-Packs und Skins werden in deinen Downloads erkannt, richtig benannt und mit einem Klick installiert.'],
    ['cpu', 'Neue Tweaks', '„Hintergrund ausbremsen im Spiel“ und vier weitere, dazu ein stärkeres WASD-Laggen.'],
    ['monitor', 'Overlays', 'Zähler im Spiel mit Kills, Spielzeit und Uhrzeit, weich eingeblendete Meldungen.'],
    ['zap', 'Minigames', 'Aim-Trainer mit drei Modi und Bestwerten.'],
    ['sun', 'Neuer Look', 'Mehr Tiefe und Bewegung, aufgeräumte Seitenleiste, Winter-Theme mit Schnee.'],
  ],
};
function whatsNew(version) {
  let seen = null; try { seen = localStorage.getItem('orbitSeenVer'); } catch { }
  if (seen === version) return;
  try { localStorage.setItem('orbitSeenVer', version); } catch { }
  let list = CHANGES[version];
  if (!list) {
    // no list built in for this version: the notes of the release that were shown before the update
    try { const n = JSON.parse(localStorage.getItem('orbitNotes') || 'null'); if (n && n.v === version && n.notes.length) list = n.notes.map(t => ['zap', t, '']); } catch { }
  }
  if (!list) return;
  modal(`<div class="m-ic">${ico('sparkle')}</div><div class="kicker">Update installiert</div><h1>Das ist neu in <span class="grad">v${esc(version)}</span></h1>
    <div class="wn">${list.map(([ic, t, d], i) => `<div class="wn-row" style="--i:${i}"><div class="qi">${ico(ic)}</div><div><b>${t}</b>${d ? `<span>${d}</span>` : ''}</div></div>`).join('')}</div>
    <div class="act"><button class="btn p lg" onclick="closeModal()">${ico('check')}Alles klar</button></div>`);
  $('#mbox').style.width = '620px';
}

/* ======================= MONTAGE: the newest kills cut together with effects ======================= */
const MO = { count: 10, music: null, busy: false, slow: true };
try { MO.slow = localStorage.getItem('orbitMoSlow') !== '0'; } catch { }
try { MO.count = +localStorage.getItem('orbitMoCount') || 10; } catch { }
function moUI() {
  if (!$('#moGo')) return;
  $$('#moCount button').forEach(b => b.classList.toggle('on', +b.dataset.v === MO.count));
  const n = (HL.n && HL.n.all) || 0, use = Math.min(n, MO.count);
  $('#moSlow').classList.toggle('on', MO.slow);
  // intro and kill counter are kept with the clip settings; the title only shows with the intro
  const intro = CL.montageIntro !== false, counter = CL.montageCounter !== false, t = $('#moTitle');
  $('#moIntro').classList.toggle('on', intro); $('#moCounter').classList.toggle('on', counter);
  if (document.activeElement !== t) t.value = CL.montageTitle || '';
  t.placeholder = (CL.name || '').trim() || 'HIGHLIGHTS'; t.disabled = !intro;
  $('#moTitleBox').classList.toggle('off', !intro); $('#moTitleBox').title = intro ? 'Leer lassen: dein Ingame-Name, ohne Namen „HIGHLIGHTS“' : 'Nur mit Intro';
  $('#moMusic span').textContent = MO.music ? MO.music.length > 22 ? MO.music.slice(0, 20) + '…' : MO.music : 'Musik wählen';
  $('#moMusic').classList.toggle('on', !!MO.music);
  $('#moMusic').title = MO.music ? 'Klick: andere Musik · Rechtsklick: ohne Musik' : 'Eine Musikdatei unter die Montage legen';
  const what = [MO.slow ? 'Zeitlupe und Zoom genau beim Kill' : 'Zoom genau beim Kill', intro && 'Intro', counter && 'Kill-Zähler im Killfeed-Stil', 'weiche Übergänge', MO.music && 'jeder Kill im Takt deiner Musik'].filter(Boolean);
  $('#moInfo').innerHTML = n >= 2 ? `<b>${use} Kills, etwa ${Math.round(use * (MO.slow ? 4.75 : 3.9) + (intro ? 3 : 0))} Sekunden.</b> ${what.join(', ')}.`
    : `Dafür braucht es mindestens zwei Kill-Clips, bisher ${n === 1 ? 'ist es einer' : 'gibt es keinen'}.`;
  const b = $('#moGo'); b.disabled = MO.busy || HL.busy || n < 2; b.innerHTML = MO.busy ? `${ico('refresh', 'i spin')}<span>Wird geschnitten…</span>` : `${ico('zap')}<span>Montage erstellen</span>`;
  if (MO.busy) $('#hlGo').disabled = true;
  $('#hlCard').classList.toggle('busy', MO.busy || HL.busy);
}
async function moMake() {
  if (MO.busy || HL.busy) return;
  MO.busy = true; moUI(); hlProgress({ pct: 1, text: 'Starte…' });
  let r = await api('clip.montage', { count: MO.count, slowmo: MO.slow, title: (CL.montageTitle || '').trim(), intro: CL.montageIntro !== false, counter: CL.montageCounter !== false }).catch(e => ({ error: e.message }));
  if (r.needFfmpeg) {
    // slow motion needs a cutting program that is loaded once
    MO.busy = false; $('#hlProg').classList.add('hidden'); moUI();
    modal(`<div class="kicker">Montage</div><h1 style="margin-top:8px">Zeitlupe braucht ein <span class="grad">Schnittprogramm</span></h1><p style="margin-top:10px;line-height:1.55">Für Zeitlupe und Zoom lädt OrbitTweak einmalig das freie Schnittprogramm ffmpeg herunter (etwa 90 MB, von gyan.dev). Danach läuft alles auf deinem PC.</p>
      <div class="act"><button class="btn" onclick="closeModal()">Abbrechen</button><button class="btn" onclick="closeModal();MO.slow=false;moUI();moMake()">Ohne Zeitlupe schneiden</button><button class="btn p" onclick="moGetFf()">${ico('download')}Laden und schneiden</button></div>`);
    return;
  }
  MO.busy = false; $('#hlProg').classList.add('hidden');
  if (r.error) toast('Keine Montage', r.error, false); else passed('Montage fertig', `${r.clips} Kills${r.bpm ? ` im Takt (${r.bpm} BPM)` : ''} · ${r.file}`);
  clipList(); hlLoad();
}
document.addEventListener('click', async e => {
  const c = e.target.closest('#moCount button');
  if (c) { MO.count = +c.dataset.v; try { localStorage.setItem('orbitMoCount', MO.count); } catch { } moUI(); return; }
  if (e.target.closest('#moMusic')) { const r = await api('clip.montageMusic', {}).catch(() => null); if (r) { MO.music = r.music; moUI(); } return; }
  if (e.target.closest('#moSlow')) { MO.slow = !MO.slow; try { localStorage.setItem('orbitMoSlow', MO.slow ? '1' : '0'); } catch { } moUI(); return; }
  if (e.target.closest('#moIntro')) { CL.montageIntro = CL.montageIntro === false; moUI(); clipPush(); return; }
  if (e.target.closest('#moCounter')) { CL.montageCounter = CL.montageCounter === false; moUI(); clipPush(); return; }
  if (e.target.closest('#moGo')) moMake();
});
// the title of the intro is kept a moment after typing stops
let moTitleT = 0;
document.addEventListener('input', e => { if (e.target.id !== 'moTitle') return; CL.montageTitle = e.target.value; clearTimeout(moTitleT); moTitleT = setTimeout(clipPush, 700); });
document.addEventListener('contextmenu', async e => {
  if (!e.target.closest('#moMusic') || !MO.music) return;
  e.preventDefault(); const r = await api('clip.montageMusic', { clear: true }).catch(() => null); if (r) { MO.music = null; moUI(); toast('Ohne Musik', 'Die Montage behält nur den Spielton'); }
});
{ const hlUI0 = hlUI; hlUI = function () { hlUI0.apply(this, arguments); moUI(); }; }
window.moGetFf = async () => {
  closeModal(); MO.busy = true; moUI(); hlProgress({ pct: 1, text: 'Lade Schnittprogramm …' });
  const r = await api('clip.ffmpeg', {}).catch(e => ({ error: e.message }));
  MO.busy = false; $('#hlProg').classList.add('hidden'); moUI();
  if (!r.ok) return toast('Nicht geladen', r.error || '', false);
  moMake();
};

/* a clip window also closes with Escape or a click beside it (only the player, not windows that ask something) */
document.addEventListener('keydown', e => { if (e.key === 'Escape' && $('#modal').classList.contains('show') && $('#mbox .player') && !CUT.busy) closeModal(); });
document.addEventListener('pointerdown', e => { if (e.target.id === 'modal' && $('#mbox .player') && !CUT.busy) closeModal(); });

/* ======================= login: paste button, auto unlock, copy the PC id ======================= */
{
  const full = () => lkInputs().every(x => x.value.length === 4);
  // a key that was pasted in one go unlocks by itself; typed keys still need Enter or the button
  const tryAuto = () => { if (full() && !LOCK.busy && $('#lock').classList.contains('show')) setTimeout(() => { if (full() && !LOCK.busy) lkSubmit(); }, 250); };
  $('#lkPaste') && ($('#lkPaste').onclick = async () => {
    let text = '';
    try { text = await navigator.clipboard.readText(); } catch { }
    const key = (text.toUpperCase().match(/ORB[- ]?[A-Z0-9]{4}[- ]?[A-Z0-9]{4}[- ]?[A-Z0-9]{4}/) || [''])[0];
    if (!key) { $('#lkErr').textContent = text ? 'In der Zwischenablage ist kein Key (ORB-XXXX-XXXX-XXXX).' : 'Die Zwischenablage ist leer. Kopier zuerst deinen Key.'; lkInputs()[0].focus(); return; }
    lkFill(key); $('#lkErr').textContent = ''; $('#lkKey').classList.remove('bad'); tryAuto();
  });
  document.addEventListener('paste', e => { if (lkInputs().includes(e.target)) setTimeout(tryAuto, 0); });
  $('#lkId') && ($('#lkId').onclick = async () => {
    const id = $('#lkHwid').textContent.trim(); if (!id || id === '…') return;
    try { await navigator.clipboard.writeText(id); } catch { return; }
    const b = $('#lkId'); b.classList.add('ok'); const c = $('#lkHwid'); const was = c.textContent; c.textContent = 'kopiert';
    setTimeout(() => { b.classList.remove('ok'); c.textContent = was; }, 1400);
  });
}
