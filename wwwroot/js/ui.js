"use strict";

/* vMon — küçük UI yardımcıları (Bootstrap JS yerine). Modal göster/gizle + ESC + dış tık. */

// CSRF: tüm aynı-origin POST/PUT/DELETE fetch'lerine antiforgery header'ı ekle
(function () {
    const meta = document.querySelector('meta[name="csrf-token"]');
    if (!meta) return;
    const token = meta.getAttribute("content");
    const origFetch = window.fetch;
    window.fetch = function (input, init) {
        init = init || {};
        const method = (init.method || (typeof input === "object" && input.method) || "GET").toUpperCase();
        const url = typeof input === "string" ? input : (input && input.url) || "";
        const sameOrigin = !/^https?:\/\//i.test(url) || url.startsWith(location.origin);
        if (method !== "GET" && method !== "HEAD" && sameOrigin) {
            const headers = new Headers(init.headers || (typeof input === "object" ? input.headers : undefined));
            if (!headers.has("X-CSRF-TOKEN")) headers.set("X-CSRF-TOKEN", token);
            init.headers = headers;
        }
        return origFetch(input, init);
    };
})();

window.openModal = function (id) {
    const el = document.getElementById(id);
    if (el) el.hidden = false;
};
window.closeModal = function (id) {
    const el = document.getElementById(id);
    if (el) el.hidden = true;
};

document.addEventListener("click", (e) => {
    const opener = e.target.closest("[data-modal-open]");
    if (opener) { window.openModal(opener.getAttribute("data-modal-open")); return; }

    const closer = e.target.closest("[data-modal-close]");
    if (closer) {
        const overlay = closer.closest(".modal-overlay");
        if (overlay) overlay.hidden = true;
        return;
    }
    // Arka plana (overlay'in kendisine) tıklayınca kapat
    if (e.target.classList && e.target.classList.contains("modal-overlay")) {
        e.target.hidden = true;
    }
});

document.addEventListener("keydown", (e) => {
    if (e.key === "Escape")
        document.querySelectorAll(".modal-overlay:not([hidden])").forEach(m => m.hidden = true);
});

/* ---- Durum kutucuğu filtresi (UP/DOWN/YAVAŞ/TOPLAM) ---- */
window.statusFilter = null;

window.statusCategory = function (s) {
    if (!s.enabled) return "other";
    if (s.isError) return "error";
    if (s.lastIsUp === true && s.slow) return "slow";
    if (s.lastIsUp === true) return "up";
    if (s.lastIsUp === false) return "down";
    return "other";
};

// Kart içi start/stop/restart butonları — yalnızca Windows/Linux Servis tiplerinde
window.serviceControlButtons = function (s) {
    if (!window.vPerms || !window.vPerms.control) return "";
    if (s.type !== "WindowsServiceControl" && s.type !== "LinuxServiceControl") return "";
    return `<div class="flex flex-wrap gap-1 mt-2 pt-2 border-t border-slate-100">
        <button class="btn btn-sm btn-success" onclick="serviceAction(${s.id},'start',this)"><i class="bi bi-play-fill"></i> Başlat</button>
        <button class="btn btn-sm btn-outline" onclick="serviceAction(${s.id},'restart',this)"><i class="bi bi-arrow-clockwise"></i> Yeniden</button>
        <button class="btn btn-sm btn-danger" onclick="serviceAction(${s.id},'stop',this)"><i class="bi bi-stop-fill"></i> Durdur</button>
    </div>`;
};

// Uzaktan servis kontrolü (yalnızca Windows/Linux Servis tipleri için kartlarda gösterilir)
window.serviceAction = async function (id, action, btn) {
    const labels = { start: "Başlat", stop: "Durdur", restart: "Yeniden Başlat" };
    if (!confirm(`'${labels[action]}' işlemi gönderilsin mi?`)) return;
    const original = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = `<i class="bi bi-arrow-repeat animate-spin"></i>`;
    try {
        const resp = await fetch(`/api/service-action/${id}`, { method: "POST", body: new URLSearchParams({ action }) });
        const data = await resp.json();
        alert((data.ok ? "✅ " : "❌ ") + data.message);
        if (typeof refreshAll === "function") await refreshAll();
        else if (typeof loadStatus === "function") await loadStatus();
    } catch (e) { alert("❌ İşlem başarısız: " + e.message); }
    finally { btn.disabled = false; btn.innerHTML = original; }
};

window.applyStatusFilter = function (services) {
    if (!window.statusFilter) return services;
    return services.filter(s => window.statusCategory(s) === window.statusFilter);
};

// onChange: filtre değişince kartları yeniden çizen geri çağırım
window.wireStatFilter = function (onChange) {
    document.querySelectorAll(".stat-btn").forEach(btn => btn.addEventListener("click", () => {
        const f = btn.dataset.filter;
        window.statusFilter = (f === "all" || window.statusFilter === f) ? null : f;
        document.querySelectorAll(".stat-btn").forEach(b => {
            const active = window.statusFilter && b.dataset.filter === window.statusFilter;
            b.classList.toggle("ring-2", active);
            b.classList.toggle("ring-brand-400", active);
            b.classList.toggle("ring-offset-1", active);
        });
        onChange();
    }));
};
