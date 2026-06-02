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
