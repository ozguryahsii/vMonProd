"use strict";

/* Tam ekran dashboard görüntüleme: durum filtresi + tek "Servis Seç" filtresi
   (4 grafiğe birden). DASHBOARD_ID global'i ViewBoard.cshtml'den gelir. */

let boardServiceIds = [];
let lastServices = [];
let chartSelection = null;
let filterBuilt = false;

const liveChart = createLiveChart(document.getElementById("liveChart"), false);
const metricCharts = {
    cpu: createMetricChart(document.getElementById("cpuChart"), false),
    ram: createMetricChart(document.getElementById("ramChart"), false),
    disk: createMetricChart(document.getElementById("diskChart"), false)
};
const HEALTH_TYPES = ["WindowsHealth", "LinuxHealth"];
const TYPE_NAMES = {
    Http: "HTTP", Tcp: "TCP", MySql: "MySQL", MsSql: "MSSQL", Oracle: "Oracle",
    Ldap: "AD/LDAP", Dns: "DNS", Sftp: "SFTP", DhcpWindowsService: "DHCP",
    Smtp: "SMTP", Imap: "IMAP", Ping: "Ping", WindowsHealth: "Win Sağlık", LinuxHealth: "Linux Sağlık",
    WindowsServiceControl: "Win Servis", LinuxServiceControl: "Linux Servis"
};

function escV(s) { const d = document.createElement("div"); d.textContent = s ?? ""; return d.innerHTML; }

async function loadBoard() {
    const resp = await fetch(`/api/dashboard-services/${DASHBOARD_ID}`);
    if (!resp.ok) throw new Error(await resp.text());
    boardServiceIds = (await resp.json()).serviceIds;
}

async function refreshAll() {
    try {
        const resp = await fetch("/api/status");
        if (!resp.ok) throw new Error(await resp.text());
        const data = await resp.json();
        lastServices = data.services.filter(s => boardServiceIds.includes(s.id));
        renderCounters(lastServices);
        buildChartFilter(lastServices);
        await renderEverything();
        document.getElementById("lastRefresh").textContent =
            "Son yenileme: " + new Date().toLocaleTimeString("tr-TR");
    } catch (e) { console.error("Dashboard yenilenemedi:", e); }
}

function effectiveChartServices() {
    let list = applyStatusFilter(lastServices);
    if (chartSelection && chartSelection.size) list = list.filter(s => chartSelection.has(s.id));
    return list;
}

async function renderEverything() {
    renderCards(lastServices);
    const minutes = parseInt(document.getElementById("rangeSelect").value);
    const chartServices = effectiveChartServices();
    await liveChart.refresh(chartServices.map(s => s.id), minutes);
    const health = chartServices.filter(s => HEALTH_TYPES.includes(s.type));
    document.getElementById("healthSection").classList.toggle("hidden", health.length === 0);
    if (health.length > 0) await refreshMetricCharts(metricCharts, health, minutes);
}

function renderCounters(services) {
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
}

function vStatus(s) {
    if (!s.enabled) return { cls: "disabled", badge: `<span class="badge pill-muted">PASİF</span>` };
    if (s.isError) return { cls: "error", badge: `<span class="badge pill-error">ERROR</span>` };
    if (s.lastIsUp === true && s.slow) return { cls: "slow", badge: `<span class="badge pill-slow">YAVAŞ</span>` };
    if (s.lastIsUp === true) return { cls: "up", badge: `<span class="badge pill-up">UP</span>` };
    if (s.lastIsUp === false) return { cls: "down", badge: `<span class="badge pill-down pulse-down">DOWN</span>` };
    return { cls: "", badge: `<span class="badge pill-muted">BEKLİYOR</span>` };
}

function renderCards(services) {
    const container = document.getElementById("serviceCards");
    if (services.length === 0) {
        container.innerHTML = `<div class="col-span-full text-center text-slate-400 py-6">Bu dashboard'a dahil servis yok. Düzenleme ekranından servis ekleyin.</div>`;
        return;
    }
    let list = applyStatusFilter(services);
    if (chartSelection && chartSelection.size) list = list.filter(s => chartSelection.has(s.id));
    if (list.length === 0) {
        container.innerHTML = `<div class="col-span-full text-center text-slate-400 py-6">Bu kategoride servis yok.</div>`;
        return;
    }
    container.innerHTML = list.map(s => {
        const { cls, badge } = vStatus(s);
        const metrics = (s.lastCpuPercent != null || s.lastRamPercent != null || s.lastMaxDiskPercent != null)
            ? `<div class="text-xs text-brand-600 font-medium mt-1"><i class="bi bi-cpu"></i> ${[
                s.lastCpuPercent != null ? "CPU %" + s.lastCpuPercent : null,
                s.lastRamPercent != null ? "RAM %" + s.lastRamPercent : null,
                s.lastMaxDiskPercent != null ? "Disk %" + s.lastMaxDiskPercent : null
              ].filter(x => x).join(" · ")}</div>` : "";
        return `
        <div class="status-card ${cls} fade-in">
            <div class="p-4">
                <div class="flex items-start justify-between gap-2">
                    <strong class="text-slate-800 truncate">${escV(s.name)}</strong>
                    ${badge}
                </div>
                <div class="mono text-slate-400 truncate mt-1">${escV(s.target)}${s.port ? ":" + s.port : ""}</div>
                ${s.description ? `<div class="text-xs text-slate-500 truncate mt-1" title="${escV(s.description)}"><i class="bi bi-card-text text-slate-400"></i> ${escV(s.description)}</div>` : ""}
                <div class="flex justify-between text-xs text-slate-400 mt-1">
                    <span>${s.lastCheckedAt ? new Date(s.lastCheckedAt).toLocaleTimeString("tr-TR") : "-"}</span>
                    <span class="font-medium text-slate-500">${s.lastResponseTimeMs != null ? s.lastResponseTimeMs + " ms" : ""}</span>
                </div>
                ${metrics}
                ${s.capacityInfo ? `<div class="text-xs text-slate-400 truncate" title="${escV(s.capacityInfo)}"><i class="bi bi-motherboard"></i> ${escV(s.capacityInfo)}</div>` : ""}
                ${s.lastError ? `<div class="text-xs text-rose-600 truncate mt-0.5" title="${escV(s.lastError)}">${escV(s.lastError)}</div>` : ""}
                ${serviceControlButtons(s)}
                <div class="flex gap-2 mt-3">
                    ${window.vPerms && window.vPerms.check ? `<button class="btn btn-outline btn-sm" onclick="checkNow(${s.id}, this)"><i class="bi bi-arrow-repeat"></i> Kontrol Et</button>` : ""}
                    <button class="btn btn-ghost btn-sm" onclick="showHistory(${s.id}, '${escV(s.name).replace(/\\/g, "\\\\").replace(/'/g, "\\'")}')"><i class="bi bi-clock-history"></i> Geçmiş</button>
                </div>
            </div>
        </div>`;
    }).join("");
}

function buildChartFilter(services) {
    if (filterBuilt) return;
    filterBuilt = true;
    const menu = document.getElementById("chartFilterMenu");
    menu.innerHTML =
        `<input id="cfSearch" class="input !py-1.5 mb-2" placeholder="Servis ara..." />
         <label class="flex items-center gap-2 px-2 py-1 text-xs text-slate-500 border-b border-slate-100 mb-1 cursor-pointer">
            <input type="checkbox" class="switch" id="cfAll" checked /> Tümünü seç / kaldır</label>
         <div id="cfList">` +
        services.map(s => `
            <label class="cf-row flex items-center gap-2 px-2 py-1.5 rounded-lg hover:bg-slate-50 cursor-pointer text-sm"
                   data-s="${escV((s.name + " " + (TYPE_NAMES[s.type] || s.type)).toLowerCase())}">
                <input class="switch cf-cb" type="checkbox" value="${s.id}" checked />
                <span class="truncate">${escV(s.name)} <span class="text-slate-400">(${TYPE_NAMES[s.type] || s.type})</span></span>
            </label>`).join("") +
        `</div>`;
    chartSelection = null;
    menu.querySelectorAll(".cf-cb").forEach(cb => cb.addEventListener("change", () => { updateChartSelection(); renderEverything(); }));
    document.getElementById("cfAll").addEventListener("change", function () {
        menu.querySelectorAll(".cf-cb").forEach(cb => cb.checked = this.checked);
        updateChartSelection(); renderEverything();
    });
    document.getElementById("cfSearch").addEventListener("input", function () {
        const q = this.value.trim().toLowerCase();
        menu.querySelectorAll(".cf-row").forEach(r => r.classList.toggle("hidden", q && !r.dataset.s.includes(q)));
    });
}

function updateChartSelection() {
    const boxes = [...document.querySelectorAll("#chartFilterMenu .cf-cb")];
    const ids = boxes.filter(cb => cb.checked).map(cb => parseInt(cb.value));
    chartSelection = ids.length === boxes.length ? null : new Set(ids);
    const all = document.getElementById("cfAll");
    if (all) { all.checked = ids.length === boxes.length; all.indeterminate = ids.length > 0 && ids.length < boxes.length; }
}

document.getElementById("rangeSelect").addEventListener("change", renderEverything);
wireStatFilter(renderEverything);
document.getElementById("btnCheckBoard")?.addEventListener("click", function () {
    checkVisible(this, applyStatusFilter(lastServices));
});

loadBoard().then(refreshAll);
setInterval(refreshAll, 30000);
