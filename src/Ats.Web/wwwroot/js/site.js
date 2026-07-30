// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

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

// Candidate drawer: htmx swaps the drawer BODY into #ats-drawer-host; wrap it in the overlay,
// and close on backdrop click, the close button, Escape, or the ats:drawer-close event.
(function () {
    var host = document.getElementById('ats-drawer-host');
    if (!host) return;

    function close() { host.innerHTML = ''; }

    document.body.addEventListener('htmx:afterSwap', function (e) {
        if (e.target.id !== 'ats-drawer-host') return;
        var body = host.innerHTML;
        host.innerHTML =
            '<div class="ats-drawer-backdrop" data-drawer-backdrop>' +
            '<div class="ats-drawer ats-drawer-in ats-scroll" role="dialog" aria-modal="true">' + body + '</div></div>';
    });

    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-drawer-close]') || e.target.hasAttribute('data-drawer-backdrop')) close();
    });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') close(); });
    document.addEventListener('ats:drawer-close', close);
})();
