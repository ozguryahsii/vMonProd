"use strict";

const REFRESH_MS = 30000;
let refreshTimer = null;

const typeIcons = {
    Http: "bi-globe", Tcp: "bi-plug", MySql: "bi-database", MsSql: "bi-database-fill",
    Oracle: "bi-database-check", Ldap: "bi-person-badge", Dns: "bi-signpost-split",
    Sftp: "bi-folder-symlink", DhcpWindowsService: "bi-router",
    Smtp: "bi-envelope", Imap: "bi-envelope-open", Ping: "bi-broadcast",
    WindowsHealth: "bi-pc-display", LinuxHealth: "bi-terminal",
    WindowsServiceControl: "bi-gear-wide-connected", LinuxServiceControl: "bi-hdd-stack"
};
const typeNames = {
    Http: "HTTP", Tcp: "TCP", MySql: "MySQL", MsSql: "MSSQL", Oracle: "Oracle",
    Ldap: "AD / LDAP", Dns: "DNS", Sftp: "SFTP", DhcpWindowsService: "DHCP",
    Smtp: "SMTP", Imap: "IMAP", Ping: "Ping",
    WindowsHealth: "Win Sağlık", LinuxHealth: "Linux Sağlık",
    WindowsServiceControl: "Win Servis", LinuxServiceControl: "Linux Servis"
};

// Kart durumu → css sınıfı + rozet (dashboard-view & dashboards-index de benzerini kullanır)
function statusInfo(s) {
    if (!s.enabled) return { cls: "disabled", badge: `<span class="badge pill-muted">PASİF</span>` };
    if (s.isError) return { cls: "error", badge: `<span class="badge pill-error">ERROR</span>` };
    if (s.lastIsUp === true && s.slow) return { cls: "slow", badge: `<span class="badge pill-slow">YAVAŞ</span>` };
    if (s.lastIsUp === true) return { cls: "up", badge: `<span class="badge pill-up">UP</span>` };
    if (s.lastIsUp === false) return { cls: "down", badge: `<span class="badge pill-down pulse-down">DOWN</span>` };
    return { cls: "", badge: `<span class="badge pill-muted">BEKLİYOR</span>` };
}

function metricsLine(s) {
    if (s.lastCpuPercent == null && s.lastRamPercent == null && s.lastMaxDiskPercent == null) return "";
    const part = (label, v) => v != null ? `${label} %${v}` : null;
    const items = [part("CPU", s.lastCpuPercent), part("RAM", s.lastRamPercent), part("Disk", s.lastMaxDiskPercent)]
        .filter(x => x).join(" · ");
    const capacity = s.capacityInfo
        ? `<div class="text-xs text-slate-400 truncate" title="${esc(s.capacityInfo)}"><i class="bi bi-motherboard"></i> ${esc(s.capacityInfo)}</div>`
        : "";
    return `<div class="text-xs text-brand-600 font-medium mt-1"><i class="bi bi-cpu"></i> ${items}</div>${capacity}`;
}

function esc(s) {
    const d = document.createElement("div");
    d.textContent = s ?? "";
    return d.innerHTML;
}
function fmtTime(iso) { return iso ? new Date(iso).toLocaleString("tr-TR") : "-"; }
function fmtDuration(fromIso, nowIso) {
    const ms = new Date(nowIso) - new Date(fromIso);
    if (ms < 0) return "-";
    const totalMin = Math.floor(ms / 60000);
    const h = Math.floor(totalMin / 60), m = totalMin % 60;
    return h > 0 ? `${h} sa ${m} dk` : `${m} dk`;
}

async function loadStatus() {
    try {
        const resp = await fetch("/api/status");
        if (!resp.ok) throw new Error(await resp.text());
        render(await resp.json());
        document.getElementById("lastRefresh").textContent =
            "Son yenileme: " + new Date().toLocaleTimeString("tr-TR");
    } catch (e) { console.error("Durum alınamadı:", e); }
}

function serviceCardHtml(s, now) {
    const { cls, badge } = statusInfo(s);
    const downInfo = s.downSince
        ? `<div class="text-xs text-rose-600 mt-1"><i class="bi bi-clock-history"></i> Kesinti: ${fmtDuration(s.downSince, now)}</div>` : "";
    const errInfo = s.lastError
        ? `<div class="text-xs text-rose-600 truncate mt-0.5" title="${esc(s.lastError)}"><i class="bi bi-exclamation-circle"></i> ${esc(s.lastError)}</div>` : "";
    return `
    <div class="status-card ${cls} fade-in">
        <div class="p-4">
            <div class="flex items-start justify-between gap-2">
                <div class="min-w-0">
                    <div class="font-semibold text-slate-800 truncate">
                        <i class="bi ${typeIcons[s.type] || "bi-question-circle"} text-brand-500"></i> ${esc(s.name)}
                    </div>
                    <span class="badge pill-type mt-1">${typeNames[s.type] || s.type}</span>
                </div>
                ${badge}
            </div>
            <div class="mono text-slate-400 truncate mt-2">${esc(s.target)}${s.port ? ":" + s.port : ""}</div>
            ${s.description ? `<div class="text-xs text-slate-500 truncate mt-1" title="${esc(s.description)}"><i class="bi bi-card-text text-slate-400"></i> ${esc(s.description)}</div>` : ""}
            <div class="flex items-center justify-between text-xs text-slate-400 mt-1">
                <span><i class="bi bi-clock"></i> ${fmtTime(s.lastCheckedAt)}</span>
                <span class="font-medium text-slate-500">${s.lastResponseTimeMs != null ? s.lastResponseTimeMs + " ms" : ""}</span>
            </div>
            ${metricsLine(s)}${downInfo}${errInfo}
            ${serviceControlButtons(s)}
            <div class="flex gap-2 mt-3">
                ${window.vPerms && window.vPerms.check ? `<button class="btn btn-outline btn-sm" onclick="checkNow(${s.id}, this)"><i class="bi bi-arrow-repeat"></i> Kontrol Et</button>` : ""}
                <button class="btn btn-ghost btn-sm" onclick="showHistory(${s.id}, '${esc(s.name).replace(/'/g, "\\'")}')"><i class="bi bi-clock-history"></i> Geçmiş</button>
            </div>
        </div>
    </div>`;
}

function render(data) {
    const container = document.getElementById("serviceCards");
    const services = data.services;
    let up = 0, down = 0, slow = 0, error = 0;
    services.forEach(s => {
        if (!s.enabled) return;
        if (s.isError) { error++; return; }
        if (s.lastIsUp === true) { up++; if (s.slow) slow++; }
        else if (s.lastIsUp === false) down++;
    });
    document.getElementById("sumUp").textContent = up;
    document.getElementById("sumDown").textContent = down;
    document.getElementById("sumError").textContent = error;
    document.getElementById("sumSlow").textContent = slow;
    document.getElementById("sumTotal").textContent = services.length;

    renderProblemBox(services.filter(s => s.enabled && (s.lastIsUp === false || s.isError)), data.now);

    if (services.length === 0) {
        container.innerHTML = `<div class="col-span-full text-center text-slate-400 py-10">
            Henüz servis tanımlanmamış. <a class="text-brand-600 hover:underline" href="/Services/Create">İlk servisi ekleyin</a>.</div>`;
        return;
    }
    window._lastData = data;
    let list = applyStatusFilter(services);
    // Grafikteki "Servis Seç" filtresi (kullanıcı seçim yaptıysa) kutucuklara da uygulansın.
    // Durum filtresi (UP/DOWN...) aktifken o öncelikli; değilse seçili servislere daral.
    if (!window.statusFilter && window.chartFilterTouched) {
        const sel = new Set(getSelectedChartIds());
        list = list.filter(s => sel.has(s.id));
    }
    container.innerHTML = list.length
        ? list.map(s => serviceCardHtml(s, data.now)).join("")
        : `<div class="col-span-full text-center text-slate-400 py-10">Bu kategoride servis yok.</div>`;
}
// Sorunlu izlemeler kutusu (grafiğin üstünde): DOWN/ERROR servisler ve sebepleri, bir bakışta
function renderProblemBox(list, now) {
    const box = document.getElementById("problemBox");
    if (!box) return;
    if (!list || !list.length) { box.classList.add("hidden"); box.innerHTML = ""; return; }
    // ERROR'lar önce, sonra DOWN; her grupta ada göre
    list.sort((a, b) => (b.isError ? 1 : 0) - (a.isError ? 1 : 0) || (a.name || "").localeCompare(b.name || ""));
    const rows = list.map(s => {
        const badge = s.isError ? '<span class="badge pill-error">ERROR</span>' : '<span class="badge pill-down">DOWN</span>';
        const reason = s.lastError ? esc(s.lastError)
            : (s.downSince ? "Kesinti: " + fmtDuration(s.downSince, now) : "—");
        return `<tr class="align-top border-b border-slate-50 last:border-0">
            <td class="py-1 pr-2 whitespace-nowrap">${badge}</td>
            <td class="py-1 pr-2 font-medium text-slate-700 align-top" title="${esc(s.name)}">
                <div class="whitespace-nowrap"><i class="bi ${typeIcons[s.type] || "bi-question-circle"} text-slate-400"></i> ${esc(s.name)}</div>
                ${s.description ? `<div class="text-[10px] font-normal text-slate-400 truncate max-w-[220px]" title="${esc(s.description)}">${esc(s.description)}</div>` : ""}</td>
            <td class="py-1 text-slate-500">${reason}</td>
        </tr>`;
    }).join("");
    box.classList.remove("hidden");
    box.innerHTML = `
      <div class="card border-l-4 border-l-rose-400">
        <div class="card-head !py-2.5">
          <span class="text-rose-600"><i class="bi bi-exclamation-triangle-fill"></i> Sorunlu İzlemeler (${list.length})</span>
          <span class="text-[11px] text-slate-400 font-normal">DOWN / ERROR · sebepleriyle</span>
        </div>
        <div class="px-4 py-2 max-h-48 overflow-y-auto">
          <table class="w-full text-[11px] leading-tight tabular-nums"><tbody>${rows}</tbody></table>
        </div>
      </div>`;
}

wireStatFilter(() => { if (window._lastData) render(window._lastData); refreshChart(); });

async function checkNow(id, btn) {
    const original = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = `<i class="bi bi-arrow-repeat animate-spin"></i>`;
    try {
        const resp = await fetch(`/api/check/${id}`, { method: "POST" });
        if (!resp.ok) throw new Error(await resp.text());
        await loadStatus();
    } catch (e) { alert("Kontrol başarısız: " + e.message); }
    finally { btn.disabled = false; btn.innerHTML = original; }
}

function tblWrap(headers, rows, empty) {
    const head = headers.map(h => `<th>${h}</th>`).join("");
    return `<div class="overflow-x-auto rounded-xl border border-slate-100">
        <table class="tbl"><thead><tr>${head}</tr></thead>
        <tbody>${rows || `<tr><td colspan="${headers.length}" class="text-slate-400">${empty}</td></tr>`}</tbody></table></div>`;
}

async function showHistory(id, name) {
    window._histId = id; window._histName = name;
    const body = document.getElementById("historyBody");
    const minutes = parseInt(document.getElementById("histRange")?.value || "1440");
    document.getElementById("historyTitle").textContent = name + " — Geçmiş";
    body.innerHTML = `<div class="text-center py-6 text-slate-400"><i class="bi bi-arrow-repeat animate-spin text-2xl"></i></div>`;
    openModal("historyModal");

    try {
        const [histResp, tsResp, mResp] = await Promise.all([
            fetch(`/api/history/${id}?take=200`),
            fetch(`/api/timeseries?ids=${id}&minutes=${minutes}`),
            fetch(`/api/metrics/${id}?minutes=${minutes}`)
        ]);
        if (!histResp.ok) throw new Error(await histResp.text());
        const data = await histResp.json();

        const outageRows = data.outages.map(o => `
            <tr>
                <td>${fmtTime(o.startedAt)}</td>
                <td>${o.endedAt ? fmtTime(o.endedAt) : '<span class="badge pill-down">devam ediyor</span>'}</td>
                <td>${o.endedAt ? fmtDuration(o.startedAt, o.endedAt) : "-"}</td>
                <td class="text-xs text-slate-400">${esc(o.firstError || "-")}</td>
            </tr>`).join("");

        const stBadge = st => st === 2 ? '<span class="badge pill-error">ERROR</span>'
            : st === 1 ? '<span class="badge pill-down">DOWN</span>' : '<span class="badge pill-up">UP</span>';
        const checkRows = data.checks.map(c => `
            <tr>
                <td>${fmtTime(c.checkedAt)}</td>
                <td>${stBadge(c.status)}</td>
                <td>${c.responseTimeMs} ms</td>
                <td class="text-xs text-slate-400">${esc(c.error || "")}</td>
            </tr>`).join("");

        // Yanıt süresi grafiği — seçili aralıktaki tüm noktalar (timeseries)
        let respChecks = [];
        try { const ts = (await tsResp.json()).series?.[0]; if (ts) respChecks = ts.points.map(p => ({ checkedAt: p.t, responseTimeMs: p.ms, isUp: p.up })); } catch { }
        const respBlock = respChecks.length
            ? `<h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-graph-up text-brand-500"></i> Yanıt Süresi</h6>
               <div style="height:200px" class="mb-5"><canvas id="respChart"></canvas></div>` : "";

        // Kaynak kullanımı (sağlık servisleri)
        let metricsBlock = "", metricsPts = null;
        try { const m = await mResp.json(); if (m.points && m.points.length) { metricsPts = m.points;
            metricsBlock = `<h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-cpu text-brand-500"></i> Kaynak Kullanımı</h6>
                <div style="height:220px" class="mb-5"><canvas id="metricsChart"></canvas></div>`; } } catch { }

        body.innerHTML = respBlock + metricsBlock + `
            <h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-exclamation-octagon text-rose-500"></i> Kesintiler</h6>
            <div class="mb-5">${tblWrap(["Başlangıç","Bitiş","Süre","İlk Hata"], outageRows, "Kesinti kaydı yok 🎉")}</div>
            <h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-list-check text-brand-500"></i> Son Kontroller</h6>
            ${tblWrap(["Zaman","Durum","Yanıt","Hata"], checkRows, "Kayıt yok")}`;

        if (respBlock) drawResponseHistory(document.getElementById("respChart"), respChecks);
        if (metricsBlock) drawMetricsHistory(document.getElementById("metricsChart"), metricsPts);
    } catch (e) {
        body.innerHTML = `<div class="alert-err">Geçmiş alınamadı: ${esc(e.message)}</div>`;
    }
}
document.getElementById("histRange")?.addEventListener("change", () => {
    if (window._histId != null) showHistory(window._histId, window._histName);
});

document.getElementById("btnCheckAll")?.addEventListener("click", async function () {
    this.disabled = true;
    this.innerHTML = `<i class="bi bi-arrow-repeat animate-spin"></i> Kontrol ediliyor...`;
    try {
        const resp = await fetch("/api/check-all", { method: "POST" });
        if (!resp.ok) throw new Error(await resp.text());
        await loadStatus();
    } catch (e) { alert("Toplu kontrol başarısız: " + e.message); }
    finally { this.disabled = false; this.innerHTML = `<i class="bi bi-arrow-repeat"></i> Tümünü Şimdi Kontrol Et`; }
});

// ---- Canlı grafik (filtreli) ----
const liveChart = createLiveChart(document.getElementById("liveChart"));
let chartFilterInitialized = false;

function getSelectedChartIds() {
    return [...document.querySelectorAll("#chartFilterMenu .cf-cb:checked")].map(cb => parseInt(cb.value));
}
function saveChartSelection() {
    localStorage.setItem("vmonitor.chartIds", JSON.stringify(getSelectedChartIds()));
}
function syncChartAll() {
    const boxes = [...document.querySelectorAll("#chartFilterMenu .cf-cb")];
    const checked = boxes.filter(cb => cb.checked).length;
    const all = document.getElementById("cfAll");
    if (all) { all.checked = boxes.length > 0 && checked === boxes.length; all.indeterminate = checked > 0 && checked < boxes.length; }
}
function buildChartFilter(services) {
    if (chartFilterInitialized) return;
    chartFilterInitialized = true;
    const menu = document.getElementById("chartFilterMenu");
    let saved = null;
    try { saved = JSON.parse(localStorage.getItem("vmonitor.chartIds")); } catch { }
    const enabledIds = services.filter(s => s.enabled).map(s => s.id);
    const selected = new Set(Array.isArray(saved) && saved.length ? saved : enabledIds.slice(0, 5));

    menu.innerHTML =
        `<input id="cfSearch" class="input !py-1.5 mb-2" placeholder="Servis ara..." />
         <label class="flex items-center gap-2 px-2 py-1 text-xs text-slate-500 border-b border-slate-100 mb-1 cursor-pointer">
            <input type="checkbox" class="switch" id="cfAll" /> Tümünü seç / kaldır</label>
         <div id="cfList">` +
        services.map(s => `
            <label class="cf-row flex items-center gap-2 px-2 py-1.5 rounded-lg hover:bg-slate-50 cursor-pointer text-sm"
                   data-s="${esc((s.name + " " + (typeNames[s.type] || s.type)).toLowerCase())}">
                <input class="switch cf-cb" type="checkbox" value="${s.id}" ${selected.has(s.id) ? "checked" : ""} />
                <span class="truncate">${esc(s.name)} <span class="text-slate-400">(${typeNames[s.type] || s.type})</span></span>
            </label>`).join("") +
        `</div>` || `<div class="text-slate-400 text-sm p-2">Servis yok</div>`;

    menu.querySelectorAll(".cf-cb").forEach(cb => cb.addEventListener("change", () => { window.chartFilterTouched = true; saveChartSelection(); syncChartAll(); refreshChart(); if (window._lastData) render(window._lastData); }));
    document.getElementById("cfAll").addEventListener("change", function () {
        menu.querySelectorAll(".cf-cb").forEach(cb => cb.checked = this.checked);
        window.chartFilterTouched = true; saveChartSelection(); refreshChart(); if (window._lastData) render(window._lastData);
    });
    document.getElementById("cfSearch").addEventListener("input", function () {
        const q = this.value.trim().toLowerCase();
        menu.querySelectorAll(".cf-row").forEach(r => r.classList.toggle("hidden", q && !r.dataset.s.includes(q)));
    });
    syncChartAll();
}
async function refreshChart() {
    try {
        let ids;
        // Durum filtresi (UP/DOWN/YAVAŞ) aktifse: o duruma uyan TÜM servisleri göster (manuel seçimi geçersiz kıl).
        // Filtre yoksa: kullanıcının "Servis Seç"ten işaretledikleri.
        if (window.statusFilter && window._lastData) {
            ids = window._lastData.services.filter(s => statusCategory(s) === window.statusFilter).map(s => s.id);
        } else {
            ids = getSelectedChartIds();
        }
        await liveChart.refresh(ids, parseInt(document.getElementById("chartRange").value));
    } catch (e) { console.error("Grafik yenilenemedi:", e); }
}
document.getElementById("chartRange").addEventListener("change", refreshChart);

async function refreshAllDashboard() { await loadStatus(); refreshChart(); }

(async () => {
    await loadStatus();
    try { buildChartFilter((await (await fetch("/api/status")).json()).services); } catch (e) { console.error(e); }
    refreshChart();
})();
refreshTimer = setInterval(refreshAllDashboard, REFRESH_MS);
