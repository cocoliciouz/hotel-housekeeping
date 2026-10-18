// Shared helpers. Data comes only from /api (ASP.NET Core). Sample data lives in /SampleData.
const ROLE = new URLSearchParams(location.search).get('as') || document.body.dataset.role || '';
const STATUSES = ['Available', 'Occupied', 'Dirty', 'Cleaning', 'Out of Service'];
const $ = s => document.querySelector(s);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const cls = s => s.toLowerCase().replace(/ /g, '-');
const badge = s => `<span class="badge ${cls(s)}">${esc(s)}</span>`;
const fmt = d => new Date(d).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' });
const msgRow = (cols, text, err) => `<tr><td colspan="${cols}" class="msg ${err ? 'error' : ''}">${esc(text)}</td></tr>`;

async function api(path, opt = {}) {
  const r = await fetch('/api' + path, {
    ...opt, headers: { 'Content-Type': 'application/json', 'X-Role': ROLE },
    body: opt.body ? JSON.stringify(opt.body) : undefined
  });
  if (!r.ok) {
    let m = r.status === 403 ? 'Not permitted for this role.' : 'Request failed.';
    try { m = (await r.json()).error || m; } catch { }
    throw new Error(m);
  }
  return r.status === 204 ? null : r.json();
}

function toast(msg, err) {
  const t = document.createElement('div');
  t.className = 'toast' + (err ? ' err' : ''); t.textContent = msg; t.setAttribute('role', err ? 'alert' : 'status');
  $('#toast').append(t); setTimeout(() => t.remove(), 3500);
}

function confirmBox(msg) {
  return new Promise(res => {
    const d = $('#confirm'); $('#confirmMsg').textContent = msg; d.returnValue = '';
    d.onclose = () => res(d.returnValue === 'ok'); d.showModal();
  });
}

function summary(rooms) {
  $('#summary').innerHTML = [['Total Rooms', rooms.length], ...STATUSES.map(s => [s, rooms.filter(r => r.status === s).length])]
    .map(([l, n]) => `<div class="card stat ${cls(l)}"><b>${n}</b><span>${l}</span></div>`).join('');
}

function filtered(rooms) {
  const q = $('#q').value.trim(), s = $('#fs').value, a = $('#fa').value;
  return rooms.filter(r => r.roomNumber.includes(q) && (!s || r.status === s) && (!a || r.availability === a));
}

// Injects toast area, confirm dialog and the search/filter bar, then wires filters to redraw().
function initShell(redraw) {
  document.body.insertAdjacentHTML('beforeend', `<div id="toast" aria-live="polite"></div>
    <dialog id="confirm"><form method="dialog"><p id="confirmMsg"></p><div class="row">
    <button class="btn" value="cancel">Cancel</button><button class="btn danger" value="ok">Confirm</button></div></form></dialog>`);
  const tb = $('#toolbar');
  tb.innerHTML = `<input id="q" type="search" placeholder="Search room number" aria-label="Search room number">
    <select id="fs" aria-label="Filter by status"><option value="">All statuses</option>${STATUSES.map(s => `<option>${s}</option>`).join('')}</select>
    <select id="fa" aria-label="Filter by availability"><option value="">Any availability</option><option>Available</option><option>Unavailable</option></select>`;
  tb.querySelectorAll('input,select').forEach(e => e.addEventListener('input', redraw));
}
