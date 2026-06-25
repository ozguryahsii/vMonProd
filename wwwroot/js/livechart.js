"use strict";

/* Paylaşılan canlı grafik: /api/timeseries verisini Chart.js çizgi grafiğine basar.
   Hem ana dashboard hem özel dashboard sayfaları kullanır.
   Not: x ekseni linear (epoch ms) — date adapter bağımlılığı yok. */

const CHART_COLORS = [
    "#0d6efd", "#198754", "#dc3545", "#fd7e14", "#6f42c1",
    "#20c997", "#d63384", "#ffc107", "#0dcaf0", "#6c757d",
    "#84563c", "#3d8bfd", "#479f76", "#e35d6a", "#feb272"
];

// Tema-duyarlı grafik renkleri (koyu temada kılavuz/eksen yazıları okunur)
function _isDarkTheme() { return document.documentElement.classList.contains("dark"); }
function chartGridColor() { return _isDarkTheme() ? "rgba(148,163,184,.18)" : "rgba(0,0,0,.05)"; }
function chartTickColor() { return _isDarkTheme() ? "#94a3b8" : "#64748b"; }
function chartLegendColor() { return _isDarkTheme() ? "#cbd5e1" : "#475569"; }
if (window.Chart) {
    Chart.defaults.color = chartTickColor();
    Chart.defaults.borderColor = chartGridColor();
}

function fmtTick(ms) {
    const d = new Date(ms);
    return d.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
}

/* Sağlık metrikleri (CPU/RAM/Disk) zaman çizelgesi: % ölçekli, servis başına bir çizgi. */
function createMetricChart(canvasEl, showLegend = true) {
    return new Chart(canvasEl, {
        type: "line",
        data: { datasets: [] },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            interaction: { mode: "nearest", intersect: false },
            scales: {
                x: { type: "linear", ticks: { maxTicksLimit: 8, callback: v => fmtTick(v) } },
                y: { min: 0, max: 100, ticks: { callback: v => "%" + v } }
            },
            plugins: {
                legend: { display: showLegend, position: "bottom", labels: { boxWidth: 12 } },
                tooltip: {
                    callbacks: {
                        title: items => items.length
                            ? new Date(items[0].parsed.x).toLocaleString("tr-TR")
                            : "",
                        label: ctx => `${ctx.dataset.label}: %${ctx.parsed.y}`
                    }
                }
            }
        }
    });
}

/* healthServices: [{id, name}] — her biri için /api/metrics çekilir,
   cpu/ram/disk grafiklerine servis başına bir çizgi basılır. */
async function refreshMetricCharts(charts, healthServices, minutes) {
    const results = await Promise.all(healthServices.map(async (s, i) => {
        const resp = await fetch(`/api/metrics/${s.id}?minutes=${minutes}`);
        if (!resp.ok) return { s, i, points: [] };
        return { s, i, points: (await resp.json()).points };
    }));

    for (const metric of ["cpu", "ram", "disk"]) {
        const chart = charts[metric];
        chart.data.datasets = results.map(({ s, i, points }) => {
            const color = CHART_COLORS[i % CHART_COLORS.length];
            return {
                label: s.name,
                data: points.filter(p => p[metric] != null)
                    .map(p => ({ x: new Date(p.t).getTime(), y: p[metric] })),
                borderColor: color, backgroundColor: color,
                borderWidth: 2, pointRadius: 0, tension: .25
            };
        });
        chart.update();
    }
}

// Dikey kılavuz çizgisi (crosshair) — fareyle hizalanır (geçmiş grafikleri ortak)
const _crosshairPlugin = {
    id: "crosshair",
    afterDatasetsDraw(c) {
        const act = c.tooltip && c.tooltip._active;
        if (!act || !act.length) return;
        const x = act[0].element.x, a = c.chartArea, cx = c.ctx;
        cx.save();
        cx.beginPath(); cx.moveTo(x, a.top); cx.lineTo(x, a.bottom);
        cx.lineWidth = 1; cx.strokeStyle = "rgba(100,116,139,.45)"; cx.setLineDash([4, 4]);
        cx.stroke(); cx.restore();
    }
};
const _vgrad = (ctx, h, hex) => {
    const g = ctx.createLinearGradient(0, 0, 0, h || 220);
    g.addColorStop(0, hex + "40"); g.addColorStop(1, hex + "00"); return g;
};
const _histTooltip = {
    backgroundColor: "rgba(15,23,42,.92)", borderColor: "rgba(255,255,255,.08)", borderWidth: 1,
    padding: 12, cornerRadius: 12, titleColor: "#fff", bodyColor: "#e2e8f0",
    usePointStyle: true, boxPadding: 6, titleFont: { weight: "600", size: 13 }, bodyFont: { size: 13 }
};

/* Geçmiş modalı için CPU/RAM/Disk grafiği — kolay hover (x ekseni), dikey kılavuz çizgisi,
   gradient dolgu, parlayan hover noktaları ve şık koyu tooltip. */
function drawMetricsHistory(canvasEl, points) {
    const ctx = canvasEl.getContext("2d");
    const grad = (hex) => _vgrad(ctx, canvasEl.height, hex);
    const mk = (label, key, color) => ({
        label,
        data: points.filter(p => p[key] != null).map(p => ({ x: new Date(p.t).getTime(), y: p[key] })),
        borderColor: color, backgroundColor: grad(color), fill: true,
        borderWidth: 2.5, pointRadius: 0, pointHoverRadius: 6,
        pointHoverBackgroundColor: color, pointHoverBorderColor: "#fff", pointHoverBorderWidth: 2,
        tension: .35
    });
    const crosshair = _crosshairPlugin;

    if (window._metricsChart) window._metricsChart.destroy();
    window._metricsChart = new Chart(canvasEl, {
        type: "line",
        data: { datasets: [mk("CPU %", "cpu", "#ed1c24"), mk("RAM %", "ram", "#10b981"), mk("Disk %", "disk", "#f59e0b")] },
        options: {
            responsive: true, maintainAspectRatio: false,
            animation: { duration: 600, easing: "easeOutQuart" },
            interaction: { mode: "index", intersect: false, axis: "x" },
            hover: { mode: "index", intersect: false },
            scales: {
                x: {
                    type: "linear", grid: { color: chartGridColor() },
                    ticks: { maxTicksLimit: 10, color: "#94a3b8", callback: v => new Date(v).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" }) }
                },
                y: { min: 0, max: 100, grid: { color: chartGridColor() }, ticks: { color: "#94a3b8", callback: v => "%" + v } }
            },
            plugins: {
                legend: { position: "bottom", labels: { usePointStyle: true, pointStyle: "circle", boxWidth: 8, padding: 16, color: chartLegendColor() } },
                tooltip: {
                    mode: "index", intersect: false,
                    backgroundColor: "rgba(15,23,42,.92)", borderColor: "rgba(255,255,255,.08)", borderWidth: 1,
                    padding: 12, cornerRadius: 12, titleColor: "#fff", bodyColor: "#e2e8f0",
                    usePointStyle: true, boxPadding: 6, titleFont: { weight: "600", size: 13 }, bodyFont: { size: 13 },
                    callbacks: {
                        title: items => items.length ? new Date(items[0].parsed.x).toLocaleString("tr-TR") : "",
                        label: ctx => `  ${ctx.dataset.label}: %${ctx.parsed.y}`
                    }
                }
            }
        },
        plugins: [crosshair]
    });
}

/* Geçmiş modalı için yanıt süresi grafiği — TÜM servis tiplerinde gösterilir.
   checks: /api/history'den gelen kontroller (en yeni önce). DOWN noktalar kırmızı. */
function drawResponseHistory(canvasEl, checks) {
    const ctx = canvasEl.getContext("2d");
    const pts = [...checks].reverse(); // kronolojik
    const data = pts.map(c => ({ x: new Date(c.checkedAt).getTime(), y: c.responseTimeMs, up: c.isUp, err: c.error }));
    const downColor = "#f43f5e", upColor = "#0d6efd";

    if (window._respChart) window._respChart.destroy();
    window._respChart = new Chart(canvasEl, {
        type: "line",
        data: {
            datasets: [{
                label: "Yanıt süresi (ms)",
                data,
                borderColor: upColor, backgroundColor: _vgrad(ctx, canvasEl.height, upColor), fill: true,
                borderWidth: 2.5, tension: .3,
                pointRadius: data.map(p => p.up === false ? 5 : 0),
                pointBackgroundColor: data.map(p => p.up === false ? downColor : upColor),
                pointBorderColor: data.map(p => p.up === false ? downColor : upColor),
                pointHoverRadius: 6, pointHoverBackgroundColor: upColor, pointHoverBorderColor: "#fff", pointHoverBorderWidth: 2
            }]
        },
        options: {
            responsive: true, maintainAspectRatio: false,
            animation: { duration: 600, easing: "easeOutQuart" },
            interaction: { mode: "index", intersect: false, axis: "x" },
            hover: { mode: "index", intersect: false },
            scales: {
                x: {
                    type: "linear", grid: { color: chartGridColor() },
                    ticks: { maxTicksLimit: 10, color: "#94a3b8", callback: v => new Date(v).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" }) }
                },
                y: { beginAtZero: true, grid: { color: chartGridColor() }, ticks: { color: "#94a3b8", callback: v => v + " ms" } }
            },
            plugins: {
                legend: { display: false },
                tooltip: Object.assign({ mode: "index", intersect: false, callbacks: {
                    title: items => items.length ? new Date(items[0].parsed.x).toLocaleString("tr-TR") : "",
                    label: ctx => `  ${ctx.parsed.y} ms` + (ctx.raw.up === false ? "  ·  DOWN" : "  ·  UP"),
                    afterLabel: ctx => ctx.raw.up === false && ctx.raw.err ? "  " + ctx.raw.err : ""
                } }, _histTooltip)
            }
        },
        plugins: [_crosshairPlugin]
    });
}

function createLiveChart(canvasEl, showLegend = true) {
    const chart = new Chart(canvasEl, {
        type: "line",
        data: { datasets: [] },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            interaction: { mode: "nearest", intersect: false },
            scales: {
                x: {
                    type: "linear",
                    ticks: { maxTicksLimit: 12, callback: v => fmtTick(v) }
                },
                y: {
                    beginAtZero: true,
                    title: { display: true, text: "Yanıt süresi (ms)" }
                }
            },
            plugins: {
                legend: { display: showLegend, position: "bottom" },
                tooltip: {
                    callbacks: {
                        title: items => items.length
                            ? new Date(items[0].parsed.x).toLocaleString("tr-TR")
                            : "",
                        label: ctx => {
                            const p = ctx.raw;
                            return `${ctx.dataset.label}: ${p.y} ms${p.up === false ? " — DOWN" : ""}`;
                        }
                    }
                }
            }
        }
    });

    return {
        chart,
        async refresh(ids, minutes) {
            if (!ids || ids.length === 0) {
                chart.data.datasets = [];
                chart.update();
                return;
            }
            const resp = await fetch(`/api/timeseries?ids=${ids.join(",")}&minutes=${minutes}`);
            if (!resp.ok) throw new Error(await resp.text());
            const data = await resp.json();

            chart.data.datasets = data.series.map((s, i) => {
                const color = CHART_COLORS[i % CHART_COLORS.length];
                const pts = s.points.map(p => ({ x: new Date(p.t).getTime(), y: p.ms, up: p.up }));
                return {
                    label: s.name,
                    data: pts,
                    borderColor: color,
                    backgroundColor: color,
                    borderWidth: 2,
                    tension: .25,
                    pointRadius: pts.map(p => p.up === false ? 5 : 2),
                    pointBackgroundColor: pts.map(p => p.up === false ? "#dc3545" : color),
                    pointBorderColor: pts.map(p => p.up === false ? "#dc3545" : color)
                };
            });
            chart.update();
        }
    };
}
