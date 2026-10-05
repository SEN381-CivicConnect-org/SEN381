(() => {
    const key = "civicconnect.theme";
    const system = window.matchMedia("(prefers-color-scheme: dark)");
    let preference;
    const readPreference = () => {
        try {
            const value = localStorage.getItem(key);
            return value === "light" || value === "dark" ? value : null;
        } catch {
            return null;
        }
    };
    const syncButtons = () => {
        const dark = document.documentElement.dataset.theme === "dark";
        document.querySelectorAll("[data-theme-toggle]").forEach(button => {
            const label = dark ? "Switch to light mode" : "Switch to dark mode";
            button.setAttribute("aria-label", label);
            button.setAttribute("title", label);
        });
    };
    const apply = () => {
        document.documentElement.dataset.theme = preference ?? (system.matches ? "dark" : "light");
        syncButtons();
    };
    preference = readPreference();
    apply();
    document.addEventListener("click", event => {
        if (!(event.target instanceof Element) || !event.target.closest("[data-theme-toggle]")) return;
        preference = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
        try { localStorage.setItem(key, preference); } catch { /* Keep switching available when storage is blocked. */ }
        apply();
    });
    system.addEventListener("change", () => { if (!preference) apply(); });
    window.addEventListener("storage", event => {
        if (event.key === key || event.key === null) { preference = readPreference(); apply(); }
    });
    document.addEventListener("DOMContentLoaded", () => {
        syncButtons();
        // Blazor can replace the layout after sign-in or navigation.
        new MutationObserver(syncButtons).observe(document.body, { childList: true, subtree: true });
    });
})();
