"use strict";

// Dil (sayfa lang özniteliği) — JS ile üretilen rapor içeriği için iki dillilik
const RPT_EN = document.documentElement.lang === "en";
const rt = (tr, en) => RPT_EN ? en : tr;

let lastSummary = null;

function esc(s) {
    const d = document.createElement("div");
    d.textContent = s ?? "";
    return d.innerHTML;
}

function fmtTime(iso) {
    return iso ? new Date(iso).toLocaleString("tr-TR") : "-";
}

function fmtMinutes(min) {
    if (min == null) return "-";
    if (min < 1) return rt("< 1 dk", "< 1 min");
    const d = Math.floor(min / 1440), h = Math.floor((min % 1440) / 60), m = Math.round(min % 60);
    let parts = [];
    if (d) parts.push(d + rt(" gün", " d"));
    if (h) parts.push(h + rt(" sa", " h"));
    if (m || parts.length === 0) parts.push(m + rt(" dk", " min"));
    return parts.join(" ");
}

function uptimeBadge(p) {
    if (p == null) return `<span class="text-slate-400">${rt("veri yok", "no data")}</span>`;
    let cls = p >= 99.9 ? "text-emerald-600" : p >= 99 ? "text-amber-600" : "text-rose-600";
    return `<strong class="${cls}">%${p.toFixed(p >= 100 ? 0 : 3)}</strong>`;
}

function getRange() {
    // datetime-local "YYYY-MM-DDTHH:MM" döner; URL'de ':' sorun olmasın diye encode edilir
    return {
        from: document.getElementById("dateFrom").value,
        to: document.getElementById("dateTo").value,
        qsFrom: encodeURIComponent(document.getElementById("dateFrom").value),
        qsTo: encodeURIComponent(document.getElementById("dateTo").value)
    };
}

function toLocalInputValue(d) {
    const pad = n => String(n).padStart(2, "0");
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function setRangeDays(days) {
    const to = new Date();
    const from = new Date(Date.now() - days * 86400000);
    document.getElementById("dateFrom").value = toLocalInputValue(from);
    document.getElementById("dateTo").value = toLocalInputValue(to);
}

// Tip adlarının okunur karşılıkları (filtre menüsü + tablo)
const reportTypeNames = {
    Http: "HTTP", Tcp: "TCP", MySql: "MySQL", MsSql: "MSSQL", Oracle: "Oracle",
    Ldap: "AD / LDAP", Dns: "DNS", Sftp: "SFTP", DhcpWindowsService: "DHCP",
    Smtp: "SMTP", Imap: "IMAP", Ping: "Ping",
    WindowsHealth: rt("Win Sağlık", "Win Health"), LinuxHealth: rt("Linux Sağlık", "Linux Health"),
    WindowsServiceControl: rt("Win Servis", "Win Service"), LinuxServiceControl: rt("Linux Servis", "Linux Service")
};

function getFilteredServices() {
    if (!lastSummary) return [];
    const text = document.getElementById("filterText").value.trim().toLowerCase();
    const type = document.getElementById("filterType").value;
    const outageOnly = document.getElementById("filterOutageOnly").checked;
    const errorOnly = document.getElementById("filterErrorOnly")?.checked;
    const keyword = document.getElementById("filterKeyword")?.value || "";
    return lastSummary.services.filter(s =>
        (!type || s.type === type) &&
        (!keyword || splitKeywords(s.keyword).some(k => k.toLowerCase() === keyword.toLowerCase())) &&
        (!outageOnly || s.outageCount > 0) &&
        (!errorOnly || (s.errorCount || 0) > 0) &&
        (!text || s.name.toLowerCase().includes(text) || (s.target || "").toLowerCase().includes(text)
            || (s.keyword || "").toLowerCase().includes(text)));
}

function buildTypeFilter() {
    const sel = document.getElementById("filterType");
    const current = sel.value;
    const types = [...new Set(lastSummary.services.map(s => s.type))].sort();
    sel.innerHTML = `<option value="">${rt("Tüm Tipler", "All Types")}</option>` +
        types.map(t => `<option value="${t}">${reportTypeNames[t] || t}</option>`).join("");
    if (types.includes(current)) sel.value = current; // seçim korunur
}

function splitKeywords(keyword) {
    return (keyword || "").split(",").map(k => k.trim()).filter(k => k);
}

function buildKeywordFilter() {
    const sel = document.getElementById("filterKeyword");
    const current = sel.value;
    const keywords = [...new Set(lastSummary.services.flatMap(s => splitKeywords(s.keyword)))].sort();
    sel.innerHTML = `<option value="">${rt("Tüm Etiketler", "All Tags")}</option>` +
        keywords.map(k => `<option value="${esc(k)}">${esc(k)}</option>`).join("");
    if (keywords.includes(current)) sel.value = current;
}

async function loadSummary() {
    const body = document.getElementById("summaryBody");
    body.innerHTML = `<tr><td colspan="8" class="text-center text-slate-400 py-6"><i class="bi bi-arrow-repeat animate-spin"></i> ${rt("Yükleniyor...", "Loading...")}</td></tr>`;
    document.getElementById("detailCard").classList.add("hidden");
    try {
        const resp = await fetch(`/api/report-summary?from=${getRange().qsFrom}&to=${getRange().qsTo}`);
        if (!resp.ok) throw new Error(await resp.text());
        lastSummary = await resp.json();
        buildTypeFilter();
        buildKeywordFilter();
        renderSummary();
    } catch (e) {
        body.innerHTML = `<tr><td colspan="8" class="text-rose-600 py-3">${rt("Rapor alınamadı", "Failed to load report")}: ${esc(e.message)}</td></tr>`;
    }
}

const HEALTH_FILTER_TYPES = ["WindowsHealth", "LinuxHealth"];

function isHealthMode() {
    return HEALTH_FILTER_TYPES.includes(document.getElementById("filterType").value);
}

// "ort / peak" hücresi — peak eşik renkleriyle vurgulanır
function avgPeakCell(avg, max) {
    if (avg == null && max == null) return `<td class="text-right text-slate-400">-</td>`;
    const cls = max == null ? "" : max >= 90 ? "text-rose-600 font-bold" : max >= 75 ? "text-amber-600 font-bold" : "text-emerald-600";
    return `<td class="text-right">%${avg ?? "-"} <span class="${cls}">/ %${max ?? "-"}</span></td>`;
}

function renderSummary() {
    const body = document.getElementById("summaryBody");
    const head = document.getElementById("summaryHead");
    const services = getFilteredServices();
    const healthMode = isHealthMode();

    document.getElementById("filterCount").textContent =
        lastSummary && services.length !== lastSummary.services.length
            ? `${services.length} / ${lastSummary.services.length} ${rt("servis", "services")}`
            : "";

    head.innerHTML = healthMode
        ? `<tr>
            <th>${rt("Servis", "Service")}</th><th>${rt("Kapasite", "Capacity")}</th>
            <th class="text-right">${rt("Kontrol", "Checks")}</th><th class="text-right">Uptime</th>
            <th class="text-right">CPU (${rt("ort", "avg")} / peak)</th><th class="text-right">RAM (${rt("ort", "avg")} / peak)</th>
            <th class="text-right">Disk (${rt("ort", "avg")} / peak)</th><th></th>
           </tr>`
        : `<tr>
            <th>${rt("Servis", "Service")}</th><th>${rt("Tip", "Type")}</th>
            <th class="text-right">${rt("Kontrol", "Checks")}</th><th class="text-right">Uptime</th>
            <th class="text-right">${rt("Kesinti", "Outages")}</th><th class="text-right">${rt("Toplam Kesinti", "Total Outage")}</th>
            <th class="text-right">${rt("Ort. Yanıt", "Avg. Response")}</th><th></th>
           </tr>`;

    if (services.length === 0) {
        body.innerHTML = `<tr><td colspan="8" class="text-center text-slate-400 py-6">${rt("Filtreye uyan servis yok.", "No services match the filter.")}</td></tr>`;
        return;
    }

    body.innerHTML = services.map(s => {
        const kwBadge = splitKeywords(s.keyword).map(k => ` <span class="badge pill-info">${esc(k)}</span>`).join("");
        const errBadge = (s.errorCount || 0) > 0 ? ` <span class="badge pill-error">${s.errorCount} ${rt("hata", "errors")}</span>` : "";
        const descLine = s.description ? `<div class="text-xs text-slate-500">${esc(s.description)}</div>` : "";
        const nameCell = `<td><div class="font-semibold text-slate-800">${esc(s.name)}${kwBadge}${errBadge}</div><div class="mono text-slate-400">${esc(s.target)}${s.port ? ":" + s.port : ""}</div>${descLine}</td>`;
        const detailBtn = `<td class="text-right"><button class="btn btn-outline btn-sm" onclick="loadDetail(${s.id})"><i class="bi bi-graph-up"></i> ${rt("Detay", "Detail")}</button></td>`;
        if (healthMode) {
            const h = s.health || {};
            return `<tr>
                ${nameCell}
                <td class="text-xs text-slate-400">${esc(s.capacityInfo || "-")}</td>
                <td class="text-right">${s.checkCount}</td>
                <td class="text-right">${uptimeBadge(s.uptimePercent)}</td>
                ${avgPeakCell(h.avgCpu, h.maxCpu)}
                ${avgPeakCell(h.avgRam, h.maxRam)}
                ${avgPeakCell(h.avgDisk, h.maxDisk)}
                ${detailBtn}
            </tr>`;
        }
        return `<tr>
                ${nameCell}
                <td><span class="badge pill-type">${reportTypeNames[s.type] || s.type}</span></td>
                <td class="text-right">${s.checkCount}</td>
                <td class="text-right">${uptimeBadge(s.uptimePercent)}</td>
                <td class="text-right">${s.outageCount > 0 ? `<span class="badge pill-down">${s.outageCount}</span>` : `<span class="text-slate-400">0</span>`}</td>
                <td class="text-right">${s.downtimeMinutes > 0 ? `<span class="text-rose-600">${fmtMinutes(s.downtimeMinutes)}</span>` : `<span class="text-slate-400">-</span>`}</td>
                <td class="text-right">${s.avgResponseMs != null ? s.avgResponseMs + " ms" : "-"}</td>
                ${detailBtn}
            </tr>`;
    }).join("");
}

async function loadDetail(id) {
    const { from, to } = getRange();
    const card = document.getElementById("detailCard");
    const body = document.getElementById("detailBody");
    card.classList.remove("hidden");
    body.innerHTML = `<div class="text-center py-6 text-slate-400"><i class="bi bi-arrow-repeat animate-spin text-2xl"></i></div>`;
    try {
        const resp = await fetch(`/api/report/${id}?from=${getRange().qsFrom}&to=${getRange().qsTo}`);
        if (!resp.ok) throw new Error(await resp.text());
        const data = await resp.json();
        document.getElementById("detailTitle").textContent =
            `${data.service.name} — ${from.replace("T", " ")} / ${to.replace("T", " ")}`;

        // Günlük erişilebilirlik şeridi (status-page tarzı)
        const strip = data.days.map(d => {
            const p = d.uptimePercent;
            const color = p == null ? "#dee2e6" : p >= 99.9 ? "#198754" : p >= 99 ? "#ffc107" : "#dc3545";
            return `<div class="uptime-cell" style="background:${color}"
                title="${d.date}&#10;Uptime: ${p != null ? "%" + p : rt("veri yok", "no data")}&#10;${rt("Kontrol", "Checks")}: ${d.checkCount} (${d.upCount} UP)&#10;${rt("Ort. yanıt", "Avg. response")}: ${d.avgResponseMs} ms"></div>`;
        }).join("");

        const dayRows = [...data.days].reverse().map(d => `
            <tr>
                <td>${d.date}</td>
                <td class="text-right">${d.checkCount}</td>
                <td class="text-right">${d.upCount}</td>
                <td class="text-right">${uptimeBadge(d.uptimePercent)}</td>
                <td class="text-right">${d.avgResponseMs} ms</td>
            </tr>`).join("");

        const outageRows = data.outages.map(o => {
            const dur = o.endedAt
                ? fmtMinutes((new Date(o.endedAt) - new Date(o.startedAt)) / 60000)
                : `<span class="badge pill-down">${rt("devam ediyor", "ongoing")}</span>`;
            return `<tr>
                <td>${fmtTime(o.startedAt)}</td>
                <td>${o.endedAt ? fmtTime(o.endedAt) : "-"}</td>
                <td>${dur}</td>
                <td class="text-xs text-slate-400">${esc(o.firstError || "-")}</td>
            </tr>`;
        }).join("");

        const tbl = (headers, rows, empty) => `<div class="overflow-x-auto rounded-xl border border-slate-100">
            <table class="tbl"><thead><tr>${headers.map(h=>`<th>${h}</th>`).join("")}</tr></thead>
            <tbody>${rows || `<tr><td colspan="${headers.length}" class="text-slate-400">${empty}</td></tr>`}</tbody></table></div>`;

        body.innerHTML = `
            <h6 class="font-semibold text-slate-700 mb-2">${rt("Günlük Erişilebilirlik", "Daily Availability")}</h6>
            <div class="uptime-strip mb-1">${strip || `<span class="text-slate-400">${rt("Bu aralıkta veri yok.", "No data in this range.")}</span>`}</div>
            <div class="flex justify-between text-xs text-slate-400 mb-5">
                <span>${data.days.length ? data.days[0].date : ""}</span>
                <span class="flex items-center gap-1">
                    <span class="legend-dot" style="background:#10b981"></span> ≥%99.9
                    <span class="legend-dot ml-2" style="background:#f59e0b"></span> ≥%99
                    <span class="legend-dot ml-2" style="background:#f43f5e"></span> &lt;%99
                </span>
                <span>${data.days.length ? data.days[data.days.length - 1].date : ""}</span>
            </div>

            <h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-exclamation-octagon text-rose-500"></i> ${rt("Kesintiler", "Outages")} (${data.outages.length})</h6>
            <div class="mb-5">${tbl([rt("Başlangıç","Start"),rt("Bitiş","End"),rt("Süre","Duration"),rt("İlk Hata","First Error")], outageRows, rt("Bu aralıkta kesinti yok 🎉", "No outages in this range 🎉"))}</div>

            <h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-calendar3 text-brand-500"></i> ${rt("Günlük Döküm", "Daily Breakdown")}</h6>
            <div class="max-h-96 overflow-y-auto">${tbl([rt("Tarih","Date"),rt("Kontrol","Checks"),"UP","Uptime",rt("Ort. Yanıt","Avg. Response")], dayRows, rt("Veri yok", "No data"))}</div>`;
        card.scrollIntoView({ behavior: "smooth" });
    } catch (e) {
        body.innerHTML = `<div class="alert-err">${rt("Detay alınamadı", "Failed to load detail")}: ${esc(e.message)}</div>`;
    }
}

function exportCsv() {
    if (!lastSummary) return;
    const { from, to } = getRange();
    const sep = ";";
    const tr = v => v != null ? String(v).replace(".", ",") : "";
    let rows;
    if (isHealthMode()) {
        rows = [["Servis", "Tip", "Hedef", "Açıklama", "Etiket", "Kapasite", "Kontrol Sayısı", "Uptime %",
                 "CPU Ort %", "CPU Peak %", "RAM Ort %", "RAM Peak %", "Disk Ort %", "Disk Peak %"].join(sep)];
        getFilteredServices().forEach(s => {
            const h = s.health || {};
            rows.push([
                `"${s.name}"`, s.type, `"${s.target}${s.port ? ":" + s.port : ""}"`,
                `"${s.description || ""}"`, `"${s.keyword || ""}"`, `"${s.capacityInfo || ""}"`, s.checkCount, tr(s.uptimePercent),
                tr(h.avgCpu), tr(h.maxCpu), tr(h.avgRam), tr(h.maxRam), tr(h.avgDisk), tr(h.maxDisk)
            ].join(sep));
        });
    } else {
        rows = [["Servis", "Tip", "Hedef", "Açıklama", "Etiket", "Kontrol Sayısı", "UP Sayısı", "Uptime %", "Kesinti Sayısı", "Kesinti (dk)", "Ort. Yanıt (ms)"].join(sep)];
        getFilteredServices().forEach(s => rows.push([
            `"${s.name}"`, s.type, `"${s.target}${s.port ? ":" + s.port : ""}"`,
            `"${s.description || ""}"`, `"${s.keyword || ""}"`, s.checkCount, s.upCount,
            tr(s.uptimePercent),
            s.outageCount, tr(s.downtimeMinutes),
            s.avgResponseMs ?? ""
        ].join(sep)));
    }
    const blob = new Blob(["﻿" + rows.join("\r\n")], { type: "text/csv;charset=utf-8" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = `vmon-rapor_${from.replace(":", "")}_${to.replace(":", "")}.csv`;
    a.click();
    URL.revokeObjectURL(a.href);
}

document.querySelectorAll("#quickRange button").forEach(btn => {
    btn.addEventListener("click", () => {
        document.querySelectorAll("#quickRange button").forEach(b => b.classList.remove("active"));
        btn.classList.add("active");
        setRangeDays(parseInt(btn.dataset.days));
        loadSummary();
    });
});
document.getElementById("btnLoad").addEventListener("click", loadSummary);
document.getElementById("btnCsv").addEventListener("click", exportCsv);

// Filtreler — veri yeniden çekilmeden tablo anında daralır/genişler
document.getElementById("filterText").addEventListener("input", renderSummary);
document.getElementById("filterType").addEventListener("change", renderSummary);
document.getElementById("filterKeyword").addEventListener("change", renderSummary);
document.getElementById("filterOutageOnly").addEventListener("change", renderSummary);
document.getElementById("filterErrorOnly").addEventListener("change", renderSummary);

setRangeDays(30);
loadSummary();
