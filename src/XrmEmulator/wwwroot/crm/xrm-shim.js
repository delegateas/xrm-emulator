// Lightweight Xrm API shim for form script execution in XrmEmulator.
// Backs getAttribute/getControl against the actual HTML form inputs.
(function () {
    "use strict";

    function findInput(name) {
        return document.querySelector(
            'input[name="' + name + '"], select[name="' + name + '"], textarea[name="' + name + '"]'
        );
    }

    function createAttribute(name) {
        var el = findInput(name);
        return {
            getName: function () { return name; },
            getValue: function () {
                if (!el) return null;
                if (el.type === "checkbox") return el.checked;
                var v = el.value;
                if (v === "") return null;
                var n = Number(v);
                return isNaN(n) ? v : n;
            },
            setValue: function (val) {
                if (!el) return;
                if (el.type === "checkbox") {
                    el.checked = !!val;
                } else {
                    el.value = val == null ? "" : val;
                }
            },
            setRequiredLevel: function () {},
            setSubmitMode: function () {},
            addOnChange: function () {},
            getFormat: function () { return null; },
            getIsDirty: function () { return false; }
        };
    }

    function createControl(name) {
        // Controls map to the form-field wrapper or the input itself
        var el = findInput(name);
        var wrapper = el ? el.closest(".form-field") : null;
        return {
            getName: function () { return name; },
            setVisible: function (visible) {
                var target = wrapper || el;
                if (target) target.style.display = visible ? "" : "none";
            },
            setDisabled: function (disabled) {
                if (el) el.disabled = disabled;
            },
            setLabel: function (label) {
                if (wrapper) {
                    var lbl = wrapper.querySelector("label");
                    if (lbl) lbl.textContent = label;
                }
            },
            getVisible: function () {
                var target = wrapper || el;
                return target ? target.style.display !== "none" : false;
            },
            refresh: function () {}
        };
    }

    var recordId = document.body.getAttribute("data-record-id") || "";
    var entityName = document.body.getAttribute("data-entity-name") || "";

    var formContext = {
        getAttribute: createAttribute,
        getControl: createControl,
        data: {
            entity: {
                getId: function () { return recordId; },
                getEntityName: function () { return entityName; }
            }
        },
        ui: {
            tabs: { get: function () { return null; } },
            setFormNotification: function () {},
            clearFormNotification: function () {}
        }
    };

    var executionContext = {
        getFormContext: function () { return formContext; }
    };

    // Xrm.Navigation stub
    var navigation = {
        openForm: function (options, params) {
            // Build a URL to our quick-create or new form route
            var entity = options.entityName || "";
            var app = document.body.getAttribute("data-app-name") || "";
            var url = "/crm/" + encodeURIComponent(app) + "/" + encodeURIComponent(entity) + "/new";
            var qs = [];
            if (options.useQuickCreateForm) qs.push("formType=quick");
            // Pass parent context from params
            for (var key in params) {
                if (params.hasOwnProperty(key)) {
                    qs.push(encodeURIComponent(key) + "=" + encodeURIComponent(params[key]));
                }
            }
            if (qs.length) url += "?" + qs.join("&");
            window.location.href = url;
            return { then: function (s, e) {} };
        }
    };

    // Expose global Xrm object
    window.Xrm = window.Xrm || {};
    window.Xrm.Page = formContext;
    window.Xrm.Navigation = navigation;

    // Store executionContext for onload calls
    window.__xrmExecutionContext = executionContext;
})();
