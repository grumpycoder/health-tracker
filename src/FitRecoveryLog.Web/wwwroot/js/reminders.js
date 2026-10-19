// Browser stand-in for the phone's local "rate your fullness" notification. A pending reminder is
// kept in localStorage so it survives a reload, and fires as a system notification (if allowed)
// while any Fit Log tab is open. Browsers can't wake a closed tab, so the Meals page also shows
// one-tap ratings for unrated meals.
window.fitlogReminders = (function () {
    const KEY = "fitlog.mealRatingReminder";
    let timer = null;

    function fire(r) {
        localStorage.removeItem(KEY);
        try {
            if ("Notification" in window && Notification.permission === "granted") {
                const n = new Notification(r.title, { body: r.body, tag: "fitlog-meal-rating" });
                n.onclick = () => { window.focus(); location.href = "meals"; n.close(); };
            }
        } catch (e) { /* notifications unavailable — the in-page prompt still shows */ }
    }

    function arm() {
        clearTimeout(timer);
        let r = null;
        try { r = JSON.parse(localStorage.getItem(KEY) || "null"); } catch (e) { }
        if (!r) return;
        timer = setTimeout(() => fire(r), Math.max(0, r.at - Date.now()));
    }

    arm();   // re-arm a reminder scheduled before a reload

    return {
        // Ask once; returns "granted" | "denied" | "default" | "unsupported".
        requestPermission: async function () {
            if (!("Notification" in window)) return "unsupported";
            if (Notification.permission === "default") return await Notification.requestPermission();
            return Notification.permission;
        },
        schedule: function (title, body, minutes) {
            localStorage.setItem(KEY, JSON.stringify({ title, body, at: Date.now() + minutes * 60000 }));
            arm();
        },
        cancel: function () { clearTimeout(timer); localStorage.removeItem(KEY); }
    };
})();
