// Themed form validation, shared by the back office, the sign-in pages and the public career site.
// Forms keep native HTML5 constraints; this replaces only the browser's bubble with a message under
// the field. The message goes into the field's asp-validation-for span when the view has one (so a
// client and a server message never show side by side), otherwise into a span created after it.
(function () {
    'use strict';

    // Tag helpers express [Required], [StringLength] and [RegularExpression] as data-val-* only.
    // Copy them onto the native constraints. Hidden fields (a removed pipeline stage row) are left
    // unconstrained, because the browser cannot focus them and the form would never submit.
    // Checkboxes are skipped: every non-nullable bool gets data-val-required, but `required` on a
    // checkbox would mean "must be ticked".
    function arm(root) {
        if (!root || !root.querySelectorAll) return;
        root.querySelectorAll('[data-val-required]:not([type="checkbox"])').forEach(function (el) {
            el.required = el.type !== 'hidden' && el.getClientRects().length > 0;
        });
        root.querySelectorAll('[data-val-length-max]').forEach(function (el) {
            if (el.maxLength < 0) el.maxLength = +el.getAttribute('data-val-length-max');
        });
        root.querySelectorAll('[data-val-length-min]').forEach(function (el) {
            if (el.minLength < 0) el.minLength = +el.getAttribute('data-val-length-min');
        });
        root.querySelectorAll('[data-val-regex-pattern]:not([pattern])').forEach(function (el) {
            el.setAttribute('pattern', el.getAttribute('data-val-regex-pattern'));
        });
        // A server-rendered error (ModelState) gets the same aria wiring as a client one.
        root.querySelectorAll('.input-validation-error').forEach(function (el) {
            var slot = messageSlot(el, false);
            if (slot && slot.textContent.trim()) link(el, slot);
        });
    }

    function messageFor(el) {
        var v = el.validity;
        if (v.valueMissing) {
            if (el.type === 'file') return 'Choose a file to upload.';
            if (el.type === 'checkbox') return 'Tick this box to continue.';
            if (el.tagName === 'SELECT' || el.type === 'radio') return 'Choose an option.';
            if (el.type === 'email') return 'Enter an email address.';
            return 'Fill in this field.';
        }
        if (v.typeMismatch && el.type === 'email') return 'Enter a valid email address.';
        if (v.typeMismatch && el.type === 'url') return 'Enter a valid web address.';
        if (v.tooLong) return 'Use ' + el.maxLength + ' characters or fewer.';
        if (v.tooShort) return 'Use at least ' + el.minLength + ' characters.';
        if (v.patternMismatch && el.getAttribute('data-val-regex')) return el.getAttribute('data-val-regex');
        return el.validationMessage;
    }

    function messageSlot(el, create) {
        var form = el.form;
        if (el.name && form) {
            var existing = form.querySelector('[data-valmsg-for="' + CSS.escape(el.name) + '"]');
            if (existing) return existing;
        }
        var next = el.nextElementSibling;
        if (next && next.hasAttribute('data-ats-error')) return next;
        if (!create) return null;
        var span = document.createElement('span');
        span.setAttribute('data-ats-error', '');
        el.insertAdjacentElement('afterend', span);
        return span;
    }

    function link(el, slot) {
        if (!slot.id) slot.id = (el.id || (el.name || 'field').replace(/\W/g, '_')) + '-error';
        slot.classList.remove('field-validation-valid');
        slot.classList.add('field-validation-error');
        slot.setAttribute('aria-live', 'polite');
        el.setAttribute('aria-invalid', 'true');
        var ids = (el.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
        if (ids.indexOf(slot.id) < 0) el.setAttribute('aria-describedby', ids.concat(slot.id).join(' '));
    }

    function clear(el) {
        if (el.getAttribute('aria-invalid') !== 'true') return;
        el.removeAttribute('aria-invalid');
        el.classList.remove('input-validation-error');
        var slot = messageSlot(el, false);
        if (!slot) return;
        slot.textContent = '';
        slot.classList.remove('field-validation-error');
        slot.classList.add('field-validation-valid');
        var ids = (el.getAttribute('aria-describedby') || '').split(/\s+/).filter(function (id) {
            return id && id !== slot.id;
        });
        if (ids.length) el.setAttribute('aria-describedby', ids.join(' '));
        else el.removeAttribute('aria-describedby');
    }

    // `invalid` does not bubble, hence the capture phase. The browser fires it for every invalid
    // field in document order within one task, so the first one of a batch takes focus.
    var focusedThisRound = false;
    document.addEventListener('invalid', function (evt) {
        var el = evt.target;
        evt.preventDefault();
        var slot = messageSlot(el, true);
        slot.textContent = messageFor(el);
        link(el, slot);
        if (!focusedThisRound) {
            focusedThisRound = true;
            el.focus();
            setTimeout(function () { focusedThisRound = false; });
        }
    }, true);

    function onEdit(evt) {
        var el = evt.target;
        if (el.validity && el.validity.valid) clear(el);
    }
    document.addEventListener('input', onEdit, true);
    document.addEventListener('change', onEdit, true);

    // Re-arm right before validation runs, so rows added or hidden by page scripts are current.
    // Enter in a text field submits by clicking the default button, so this covers it too.
    document.addEventListener('click', function (evt) {
        var btn = evt.target.closest && evt.target.closest('button, input[type="submit"], input[type="image"]');
        if (btn && btn.type === 'submit' && btn.form && !btn.formNoValidate) arm(btn.form);
    }, true);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { arm(document); });
    } else {
        arm(document);
    }
    // htmx raises htmx:load for every swapped-in fragment (boosted navigation, drawers).
    document.addEventListener('htmx:load', function (evt) { arm(evt.target); });
})();
