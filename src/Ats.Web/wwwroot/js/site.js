// Shared front-end helpers live on the `Ats` namespace. Anything that enhances server-rendered
// markup must expose a re-apply function taking a scope, so it can run again after an htmx swap.
window.Ats = window.Ats || {};

// Disable submit buttons on form submit to prevent double-posts and signal progress.
document.addEventListener('submit', function (e) {
    var form = e.target;
    if (!(form instanceof HTMLFormElement)) return;
    var btn = form.querySelector('button[type="submit"], input[type="submit"]');
    if (btn && !btn.disabled) {
        // Let the form post first, then disable on the next tick.
        setTimeout(function () { btn.disabled = true; btn.classList.add('disabled'); }, 0);
    }
}, true);

// Ctrl/Cmd+K focuses global search; Escape clears the result list.
(function () {
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
            var input = document.getElementById('ats-global-search');
            if (!input) return;
            e.preventDefault();
            input.focus();
            input.select();
        }
        if (e.key === 'Escape') {
            var results = document.getElementById('ats-search-results');
            if (results) results.innerHTML = '';
        }
    });
    // Clicking away closes the result list.
    document.addEventListener('click', function (e) {
        var wrap = document.querySelector('.ats-topbar-search');
        var results = document.getElementById('ats-search-results');
        if (!wrap || !results) return;
        if (!wrap.contains(e.target)) results.innerHTML = '';
    });
})();

// Localise UTC timestamps to the viewer's timezone. The server renders <time datetime data-local>
// with a UTC fallback (see LocalTimeTagHelper); here we rewrite the text to the browser's own zone.
// Re-runs after htmx swaps so drawer/delivery-row fragments are localised too.
(function () {
    // Parts are assembled explicitly rather than using the locale's default pattern, so the rendered
    // string keeps the product's day-first, 24-hour house format (e.g. 30/06 15:22) and its compact
    // column width on every machine. Only the ZONE follows the viewer; the format never does.
    function parts(d, kind) {
        var o = { hour12: false, timeZone: undefined };
        if (kind === 'weekday') { o.weekday = 'long'; o.day = 'numeric'; o.month = 'long'; }
        else if (kind === 'date' || kind === 'monthday') { o.day = '2-digit'; o.month = 'short'; }
        else { o.day = '2-digit'; o.month = '2-digit'; o.hour = '2-digit'; o.minute = '2-digit'; }
        if (kind === 'date' || kind === 'datetime') o.year = 'numeric';
        var got = {};
        new Intl.DateTimeFormat('en-GB', o).formatToParts(d).forEach(function (p) { got[p.type] = p.value; });
        return got;
    }
    function render(d, kind) {
        var p = parts(d, kind);
        switch (kind) {
            case 'weekday': return p.weekday + ' ' + p.day + ' ' + p.month;
            case 'date': return p.day + ' ' + p.month + ' ' + p.year;
            case 'monthday': return p.day + ' ' + p.month;
            case 'time': return p.hour + ':' + p.minute;
            case 'short': return p.day + '/' + p.month + ' ' + p.hour + ':' + p.minute;
            default: return p.day + '/' + p.month + '/' + p.year + ' ' + p.hour + ':' + p.minute;
        }
    }
    function localise(root) {
        (root || document).querySelectorAll('time[data-local]').forEach(function (el) {
            var iso = el.getAttribute('datetime');
            if (!iso) return;
            var d = new Date(iso);
            if (isNaN(d.getTime())) return;
            try { el.textContent = render(d, el.getAttribute('data-local')); }
            catch (e) { /* leave the UTC fallback in place */ }
        });
    }
    document.addEventListener('DOMContentLoaded', function () { localise(document); });
    document.body.addEventListener('htmx:afterSwap', function (e) { localise(e.target); });
    Ats.localiseTimes = localise;
    window.atsLocaliseTimes = localise;   // back-compat alias
})();

// Candidate drawer: htmx swaps the drawer BODY into #ats-drawer-host; this wraps it in the overlay
// and gives it real modal behaviour (A11Y-2). Previously it claimed role="dialog" aria-modal="true"
// while focus never entered it, Tab escaped to the page behind, focus was never returned on close,
// and it had no accessible name.
(function () {
    var host = document.getElementById('ats-drawer-host');
    if (!host) return;

    var FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]),' +
                    ' textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
    var lastFocused = null;

    function panel() { return host.querySelector('.ats-drawer'); }

    function focusables() {
        var p = panel();
        if (!p) return [];
        return Array.prototype.filter.call(p.querySelectorAll(FOCUSABLE), function (el) {
            return el.offsetParent !== null || el === document.activeElement;
        });
    }

    function close() {
        if (!host.innerHTML) return;
        host.innerHTML = '';
        // Return focus where the user left it, so the keyboard position is not lost.
        if (lastFocused && document.contains(lastFocused)) {
            try { lastFocused.focus(); } catch (e) { /* element may have been swapped away */ }
        }
        lastFocused = null;
    }
    Ats.closeDrawer = close;

    // Remember the trigger before the request starts, while it is still focused.
    Ats.rememberDrawerTrigger = function (el) {
        lastFocused = el || document.activeElement;
    };

    document.body.addEventListener('htmx:afterSwap', function (e) {
        if (e.target.id !== 'ats-drawer-host') return;
        var body = host.innerHTML;
        host.innerHTML =
            '<div class="ats-drawer-backdrop" data-drawer-backdrop>' +
            '<div class="ats-drawer ats-drawer-in ats-scroll" role="dialog" aria-modal="true"' +
            ' aria-labelledby="ats-drawer-title" tabindex="-1">' + body + '</div></div>';

        // Move focus into the dialog: the close button if present, otherwise the panel itself.
        var p = panel();
        if (!p) return;
        var closeBtn = p.querySelector('[data-drawer-close]');
        (closeBtn || p).focus();
    });

    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-drawer-close]') || e.target.hasAttribute('data-drawer-backdrop')) close();
    });

    document.addEventListener('keydown', function (e) {
        if (!host.innerHTML) return;

        if (e.key === 'Escape') { close(); return; }

        // Trap Tab inside the dialog, which is what aria-modal="true" promises.
        if (e.key !== 'Tab') return;
        var items = focusables();
        if (items.length === 0) { e.preventDefault(); return; }
        var first = items[0];
        var last = items[items.length - 1];
        var p = panel();
        var inside = p && p.contains(document.activeElement);

        if (!inside) { e.preventDefault(); first.focus(); return; }
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    });

    document.addEventListener('ats:drawer-close', close);
})();

// ---------------------------------------------------------------------------------------------
// Boosted navigation (NAV-1). hx-boost on <body> swaps only #ats-content, so the shell, CSS,
// fonts and the global scripts are never re-fetched or re-executed. This module supplies the
// pieces htmx cannot infer on its own, and the safety nets that keep every existing flow working.
// ---------------------------------------------------------------------------------------------
(function () {
    var CONTENT_ID = 'ats-content';

    function isBoosted(evt) {
        var cfg = evt.detail && evt.detail.requestConfig;
        return !!(cfg && cfg.boosted);
    }

    // A real navigation, used whenever the response is not a back-office page we can swap into.
    function hardNavigate(url) {
        if (url) window.location.href = url;
        else window.location.reload();
    }

    // Only the content fragment is swapped, so the document title has to be carried across by hand.
    // The server stamps it on #ats-content (data-page-title), which also covers htmx's history
    // restore — that replays cached DOM with no HTTP response to read a <title> from.
    function syncTitleFromContent() {
        var el = document.getElementById(CONTENT_ID);
        var t = el && el.getAttribute('data-page-title');
        if (t) document.title = t;
    }

    document.body.addEventListener('htmx:beforeSwap', function (evt) {
        if (!isBoosted(evt)) return;
        var xhr = evt.detail.xhr;
        var contentType = (xhr.getResponseHeader('Content-Type') || '').toLowerCase();
        var body = xhr.responseText || '';

        // Anything that is not a back-office HTML page — the login page after sign-out or session
        // expiry, an error/status page (different layout), a file download — cannot be swapped into
        // #ats-content. Hand it to the browser instead of silently doing nothing.
        if (contentType.indexOf('text/html') === -1 || body.indexOf('id="' + CONTENT_ID + '"') === -1) {
            evt.detail.shouldSwap = false;
            hardNavigate(xhr.responseURL || (evt.detail.pathInfo && evt.detail.pathInfo.requestPath));
            return;
        }
    });

    // Title after a boosted swap, and after Back/Forward restores cached content.
    document.body.addEventListener('htmx:afterSwap', function (evt) {
        if (evt.target && evt.target.id === CONTENT_ID) syncTitleFromContent();
    });
    document.body.addEventListener('htmx:historyRestore', syncTitleFromContent);

    // Non-2xx (404/403/500) would otherwise leave the user on the old page with nothing happening.
    document.body.addEventListener('htmx:responseError', function (evt) {
        if (!isBoosted(evt)) return;
        var xhr = evt.detail.xhr;
        hardNavigate(xhr.responseURL || (evt.detail.pathInfo && evt.detail.pathInfo.requestPath));
    });

    // Connection dropped mid-navigation: fall back so the user is not stranded.
    document.body.addEventListener('htmx:sendError', function (evt) {
        if (!isBoosted(evt)) return;
        hardNavigate(evt.detail.pathInfo && evt.detail.pathInfo.requestPath);
    });

    // A drawer left open over a freshly swapped page would be stale.
    document.body.addEventListener('htmx:beforeRequest', function (evt) {
        if (!isBoosted(evt)) return;
        var host = document.getElementById('ats-drawer-host');
        if (host) host.innerHTML = '';
    });

    // jQuery Unobtrusive Validation binds once on initial load. Forms that arrive in a swap must be
    // parsed explicitly, or client-side validation silently stops working after the first navigation.
    document.body.addEventListener('htmx:afterSettle', function (evt) {
        var $ = window.jQuery;
        if (!$ || !$.validator || !$.validator.unobtrusive) return;
        var scope = evt.target && evt.target.querySelectorAll ? evt.target : document;
        scope.querySelectorAll('form').forEach(function (form) {
            try {
                $(form).removeData('validator').removeData('unobtrusiveValidation');
                $.validator.unobtrusive.parse(form);
            } catch (e) { /* a form without validation metadata is fine */ }
        });
    });
})();

// ---------------------------------------------------------------------------------------------
// Drawer skeleton (UX-2). Painted synchronously on click so the drawer is never blank while the
// fetch is in flight. The real content replaces it via the normal htmx swap.
// ---------------------------------------------------------------------------------------------
(function () {
    Ats.showDrawerSkeleton = function () {
        var host = document.getElementById('ats-drawer-host');
        if (!host) return;
        host.innerHTML =
            '<div class="ats-drawer-backdrop" data-drawer-backdrop>' +
            '<div class="ats-drawer ats-scroll" role="dialog" aria-modal="true" aria-busy="true">' +
            '<div class="ats-skeleton-stack">' +
            '<div class="ats-skeleton ats-skeleton--title"></div>' +
            '<div class="ats-skeleton ats-skeleton--line"></div>' +
            '<div class="ats-skeleton ats-skeleton--line" style="width:70%"></div>' +
            '<div class="ats-skeleton ats-skeleton--block"></div>' +
            '</div></div></div>';
    };
})();

// ---------------------------------------------------------------------------------------------
// Toasts + board reconciliation (UX-3). A move that fails on the network or server used to leave
// the card sitting in the column the user dropped it into, silently disagreeing with the server.
// ---------------------------------------------------------------------------------------------
(function () {
    function toast(message, tone) {
        var host = document.getElementById('ats-toasts');
        if (!host) {
            host = document.createElement('div');
            host.id = 'ats-toasts';
            host.className = 'ats-toasts';
            host.setAttribute('aria-live', 'polite');
            document.body.appendChild(host);
        }
        var el = document.createElement('div');
        el.className = 'ats-toast ats-toast--' + (tone || 'danger');
        el.setAttribute('role', 'status');
        el.textContent = message;
        host.appendChild(el);
        setTimeout(function () { el.classList.add('ats-toast--out'); }, 5000);
        setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 5400);
    }
    Ats.toast = toast;

    // Pull the board back to server truth after a failed move.
    function reconcileBoard() {
        var board = document.getElementById('board-container');
        if (!board || !window.htmx) return;
        htmx.ajax('GET', window.location.pathname + window.location.search,
            { target: '#board-container', select: '#board-container', swap: 'outerHTML' });
    }

    function isBoardMove(evt) {
        var el = evt.detail && evt.detail.elt;
        return !!(el && el.classList && el.classList.contains('ats-board-card'));
    }

    document.body.addEventListener('htmx:responseError', function (evt) {
        if (!isBoardMove(evt)) return;
        toast('That move could not be saved. The board has been refreshed.', 'danger');
        reconcileBoard();
    });

    document.body.addEventListener('htmx:sendError', function (evt) {
        if (!isBoardMove(evt)) return;
        toast('Connection lost, so the move was not saved. The board has been refreshed.', 'danger');
        reconcileBoard();
    });
})();
