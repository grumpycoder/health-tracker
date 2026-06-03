// Audio cue for the hold (plank) countdown. The AudioContext is created/resumed
// from a user gesture (the Start tap) so iOS WKWebView allows playback when the
// countdown later fires from a timer.
(function () {
    let ctx;
    function ensure() {
        if (!ctx) {
            const AC = window.AudioContext || window.webkitAudioContext;
            if (AC) ctx = new AC();
        }
        if (ctx && ctx.state === "suspended") ctx.resume();
        return ctx;
    }
    function tone(at, freq, dur) {
        const o = ctx.createOscillator(), g = ctx.createGain();
        o.connect(g); g.connect(ctx.destination);
        o.type = "sine"; o.frequency.value = freq;
        g.gain.setValueAtTime(0.0001, at);
        g.gain.exponentialRampToValueAtTime(0.5, at + 0.02);
        g.gain.exponentialRampToValueAtTime(0.0001, at + dur);
        o.start(at); o.stop(at + dur);
    }
    // Notify .NET when the app/page becomes visible again (e.g. resumed next day).
    window.appLifecycle = {
        onResume(dotNetRef) {
            document.addEventListener("visibilitychange", () => {
                if (document.visibilityState === "visible") {
                    try { dotNetRef.invokeMethodAsync("OnAppResumed"); } catch (e) { }
                }
            });
        }
    };

    // Touch drag-and-drop reordering (SortableJS). On drop we revert the DOM
    // move and report indexes to .NET — Blazor re-renders from the new state,
    // keeping its render tree in sync.
    window.exerciseSort = {
        init(elementId, dotNetRef) {
            const el = document.getElementById(elementId);
            if (!el) return;
            if (el._sortable) el._sortable.destroy();
            el._sortable = new Sortable(el, {
                handle: ".drag-handle",
                animation: 150,
                onUpdate(evt) {
                    evt.item.remove();
                    evt.to.insertBefore(evt.item, evt.to.childNodes[evt.oldIndex]);
                    dotNetRef.invokeMethodAsync("OnReorder", evt.oldIndex, evt.newIndex);
                }
            });
        }
    };

    window.holdTimer = {
        arm() { try { ensure(); } catch (e) { } },
        beep() {
            try {
                if (!ensure()) return;
                const t = ctx.currentTime;
                tone(t, 880, 0.18);
                tone(t + 0.22, 880, 0.18);
                tone(t + 0.44, 1175, 0.35);
            } catch (e) { }
        }
    };
})();
