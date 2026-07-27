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
