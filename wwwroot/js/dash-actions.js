"use strict";

/* Dashboard'lar ve tam ekran görünüm için ortak "Kontrol Et" + "Geçmiş" eylemleri.
   (Ana dashboard kendi dashboard.js'inde aynı işlevlere sahip.) */

function _da_esc(s) { const d = document.createElement("div"); d.textContent = s ?? ""; return d.innerHTML; }
function _da_time(iso) { return iso ? new Date(iso).toLocaleString("tr-TR") : "-"; }
function _da_dur(a, b) {
    const ms = new Date(b) - new Date(a);
    if (ms < 0) return "-";
    const m = Math.floor(ms / 60000), h = Math.floor(m / 60);
    return h > 0 ? `${h} sa ${m % 60} dk` : `${m} dk`;
}
function _da_tbl(headers, rows, empty) {
    return `<div class="overflow-x-auto rounded-xl border border-slate-100">
        <table class="tbl"><thead><tr>${headers.map(h => `<th>${h}</th>`).join("")}</tr></thead>
        <tbody>${rows || `<tr><td colspan="${headers.length}" class="text-slate-400">${empty}</td></tr>`}</tbody></table></div>`;
}

async function checkNow(id, btn) {
    const original = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = `<i class="bi bi-arrow-repeat animate-spin"></i>`;
    try {
        const resp = await fetch(`/api/check/${id}`, { method: "POST" });
        if (!resp.ok) throw new Error(await resp.text());
        if (typeof refreshAll === "function") await refreshAll();
    } catch (e) { alert("Kontrol başarısız: " + e.message); }
    finally { btn.disabled = false; btn.innerHTML = original; }
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
            <tr><td>${_da_time(o.startedAt)}</td>
                <td>${o.endedAt ? _da_time(o.endedAt) : '<span class="badge pill-down">devam ediyor</span>'}</td>
                <td>${o.endedAt ? _da_dur(o.startedAt, o.endedAt) : "-"}</td>
                <td class="text-xs text-slate-400">${_da_esc(o.firstError || "-")}</td></tr>`).join("");
        const stBadge = st => st === 2 ? '<span class="badge pill-error">ERROR</span>'
            : st === 1 ? '<span class="badge pill-down">DOWN</span>' : '<span class="badge pill-up">UP</span>';
        const checkRows = data.checks.map(c => `
            <tr><td>${_da_time(c.checkedAt)}</td>
                <td>${stBadge(c.status)}</td>
                <td>${c.responseTimeMs} ms</td>
                <td class="text-xs text-slate-400">${_da_esc(c.error || "")}</td></tr>`).join("");

        let respChecks = [];
        try { const ts = (await tsResp.json()).series?.[0]; if (ts) respChecks = ts.points.map(p => ({ checkedAt: p.t, responseTimeMs: p.ms, isUp: p.up })); } catch { }
        const respBlock = respChecks.length
            ? `<h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-graph-up text-brand-500"></i> Yanıt Süresi</h6>
               <div style="height:200px" class="mb-5"><canvas id="respChart"></canvas></div>` : "";

        let metricsBlock = "", metricsPts = null;
        try { const m = await mResp.json(); if (m.points && m.points.length) { metricsPts = m.points;
            metricsBlock = `<h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-cpu text-brand-500"></i> Kaynak Kullanımı</h6>
                <div style="height:220px" class="mb-5"><canvas id="metricsChart"></canvas></div>`; } } catch { }

        body.innerHTML = respBlock + metricsBlock + `
            <h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-exclamation-octagon text-rose-500"></i> Kesintiler</h6>
            <div class="mb-5">${_da_tbl(["Başlangıç", "Bitiş", "Süre", "İlk Hata"], outageRows, "Kesinti kaydı yok 🎉")}</div>
            <h6 class="font-semibold text-slate-700 mb-2"><i class="bi bi-list-check text-brand-500"></i> Son Kontroller</h6>
            ${_da_tbl(["Zaman", "Durum", "Yanıt", "Hata"], checkRows, "Kayıt yok")}`;

        if (respBlock) drawResponseHistory(document.getElementById("respChart"), respChecks);
        if (metricsBlock) drawMetricsHistory(document.getElementById("metricsChart"), metricsPts);
    } catch (e) {
        body.innerHTML = `<div class="alert-err">Geçmiş alınamadı: ${_da_esc(e.message)}</div>`;
    }
}
document.getElementById("histRange")?.addEventListener("change", () => {
    if (window._histId != null) showHistory(window._histId, window._histName);
});

/* Görünen (durum filtresine uyan) servisleri toplu kontrol et. */
async function checkVisible(btn, services) {
    const ids = services.map(s => s.id);
    if (ids.length === 0) return;
    const original = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = `<i class="bi bi-arrow-repeat animate-spin"></i> Kontrol ediliyor (${ids.length})...`;
    try {
        const resp = await fetch("/api/check-ids", { method: "POST", body: new URLSearchParams({ ids: ids.join(",") }) });
        if (!resp.ok) throw new Error(await resp.text());
        if (typeof refreshAll === "function") await refreshAll();
    } catch (e) { alert("Toplu kontrol başarısız: " + e.message); }
    finally { btn.disabled = false; btn.innerHTML = original; }
}
