(function () {
  "use strict";

  var STORAGE_KEY = "tiaRest.apiKey";
  var CONTEXT_PARAMS = {
    processid: true,
    projectname: true,
    kind: true,
    devicename: true,
    deviceitemname: true
  };

  var els = {
    apiKey: document.getElementById("apiKey"),
    btnToggleKey: document.getElementById("btnToggleKey"),
    btnRefreshContext: document.getElementById("btnRefreshContext"),
    selProject: document.getElementById("selProject"),
    selPlc: document.getElementById("selPlc"),
    selHmi: document.getElementById("selHmi"),
    contextStatus: document.getElementById("contextStatus"),
    selEndpoint: document.getElementById("selEndpoint"),
    methodBadge: document.getElementById("methodBadge"),
    endpointSummary: document.getElementById("endpointSummary"),
    chkFillContext: document.getElementById("chkFillContext"),
    queryParams: document.getElementById("queryParams"),
    bodyField: document.getElementById("bodyField"),
    requestBody: document.getElementById("requestBody"),
    bodyHint: document.getElementById("bodyHint"),
    btnSend: document.getElementById("btnSend"),
    requestUrl: document.getElementById("requestUrl"),
    responseMeta: document.getElementById("responseMeta"),
    responseBody: document.getElementById("responseBody")
  };

  var state = {
    help: null,
    projects: [],
    plcs: [],
    hmis: [],
    endpoints: [],
    selected: null,
    paramValues: {}
  };

  function apiKey() {
    return (els.apiKey.value || "").trim();
  }

  function setStatus(text, kind) {
    els.contextStatus.textContent = text || "";
    els.contextStatus.className = "status " + (kind || "muted");
  }

  function headers(needsKey) {
    var h = { Accept: "application/json, text/plain, */*" };
    if (needsKey !== false) {
      var key = apiKey();
      if (!key) {
        throw new Error("Bitte API-Key eingeben.");
      }
      h["X-API-Key"] = key;
    }
    return h;
  }

  async function apiFetch(path, options) {
    options = options || {};
    var init = {
      method: options.method || "GET",
      headers: headers(options.needsKey)
    };
    if (options.body != null) {
      init.headers["Content-Type"] = "application/json; charset=utf-8";
      init.body = options.body;
    }
    var response = await fetch(path, init);
    var text = await response.text();
    return { response: response, text: text };
  }

  function parseJsonOrThrow(text, fallbackMessage) {
    try {
      return JSON.parse(text);
    } catch (e) {
      throw new Error(fallbackMessage || text || "Ungültige JSON-Antwort.");
    }
  }

  function selectedProject() {
    var idx = els.selProject.selectedIndex;
    if (idx <= 0) return null;
    return state.projects[idx - 1] || null;
  }

  function selectedPlc() {
    var idx = els.selPlc.selectedIndex;
    if (idx <= 0) return null;
    return state.plcs[idx - 1] || null;
  }

  function selectedHmi() {
    var idx = els.selHmi.selectedIndex;
    if (idx <= 0) return null;
    return state.hmis[idx - 1] || null;
  }

  function usesHmiDevice(endpoint) {
    if (!endpoint) return false;
    var path = (endpoint.Path || "").toLowerCase();
    if (path.indexOf("/api/hmi/") === 0 || path.indexOf("/hmi/") >= 0) {
      return true;
    }
    var group = (endpoint.Group || "").toLowerCase();
    return group.indexOf("hmi") >= 0;
  }

  function contextForEndpoint(endpoint) {
    var project = selectedProject();
    var ctx = {};
    if (project) {
      ctx.processId = String(project.ProcessId);
      ctx.projectName = project.Name;
      if (project.Kind) {
        ctx.kind = project.Kind;
      }
    }
    var device = usesHmiDevice(endpoint) ? selectedHmi() : selectedPlc();
    if (device) {
      ctx.deviceName = device.DeviceName;
      ctx.deviceItemName = device.DeviceItemName;
    }
    return ctx;
  }

  function contextValue(ctx, name) {
    if (!ctx || !name) return "";
    if (Object.prototype.hasOwnProperty.call(ctx, name)) return ctx[name];
    var lower = name.toLowerCase();
    var key = Object.keys(ctx).find(function (k) { return k.toLowerCase() === lower; });
    return key ? ctx[key] : "";
  }

  function isContextParam(name) {
    return !!(name && CONTEXT_PARAMS[name.toLowerCase()]);
  }

  function paramType(param) {
    return ((param && param.Type) || "string").toLowerCase();
  }

  function shortPlaceholder(name, description) {
    var n = (name || "").toLowerCase();
    if (n === "rootdirectory" || n === "directory") return "z. B. C:\\TiaFiles\\…";
    if (n === "path") return "TIA-Ordnernamen, kommagetrennt";
    if (n === "devicename") return "DeviceName";
    if (n === "deviceitemname") return "DeviceItemName";
    if (n === "targetdirectory") return "Dateisystem-Zielordner";
    return description || "";
  }

  function exampleItems(description) {
    var item = {
      Directory: "C:\\TiaWorkspace\\Project\\PLC\\Folder"
    };
    var desc = description || "";
    if (/BlockName/i.test(desc)) item.BlockName = "MyBlock";
    if (/UdtName/i.test(desc)) item.UdtName = "MyUdt";
    if (/TagTableName/i.test(desc)) item.TagTableName = "MyTagTable";
    if (/Path/i.test(desc)) item.Path = ["Folder1"];
    if (/Format/i.test(desc)) item.Format = "SimaticData/SD";
    if (/BlockNumber/i.test(desc)) item.BlockNumber = 1;
    if (!item.BlockName && !item.UdtName && !item.TagTableName) {
      item.Name = "MyElement";
      item.Path = ["Folder1"];
    }
    return [item];
  }

  function exampleValue(param, ctx, fillContext) {
    var name = param.Name;
    var type = paramType(param);
    var lower = (name || "").toLowerCase();

    if (fillContext && isContextParam(name)) {
      var raw = contextValue(ctx, name);
      if (type === "number") {
        var n = Number(raw);
        return Number.isNaN(n) ? 0 : n;
      }
      return raw || "";
    }

    if (type === "number") return 0;
    if (type === "boolean") return true;
    if (type === "string[]") return ["Folder1", "Folder2"];
    if (type === "json") {
      if (lower === "items") return exampleItems(param.Description);
      return {};
    }
    if (lower === "directory" || lower === "rootdirectory" || lower === "targetdirectory") {
      return "C:\\TiaWorkspace\\Project\\PLC\\Folder";
    }
    if (lower === "format") return "SimaticData/SD";
    if (lower === "librarymode") return "InTest";
    if (lower === "libraryversion") return "0.1.0";
    if (lower === "libraryauthor") return "";
    if (lower === "librarycomment") return "";
    if (lower === "blockname") return "MyBlock";
    if (lower === "udtname") return "MyUdt";
    if (lower === "tagtablename") return "MyTagTable";
    if (lower === "content") return "";
    if (lower === "multilingualtext") return null;
    return "";
  }

  function buildExampleBody(endpoint, ctx, fillContext) {
    var params = (endpoint && endpoint.BodyParams) || [];
    if (params.length === 0) {
      return {};
    }
    var obj = {};
    params.forEach(function (param) {
      obj[param.Name] = exampleValue(param, ctx, fillContext);
    });
    return obj;
  }

  function bodyHintText(endpoint) {
    var params = (endpoint && endpoint.BodyParams) || [];
    if (params.length > 0) {
      return "Vorlage aus der API-Doku · bekannte Felder aus dem Kontext · übrige Felder als Beispiel editierbar. "
        + params.map(function (p) {
          var t = paramType(p);
          return p.Name + (t !== "string" ? " (" + t + ")" : "");
        }).join(", ");
    }
    return (endpoint && endpoint.Body) || "JSON-Body gemäß Endpunktbeschreibung.";
  }

  function writeBodyTemplate(force) {
    var ep = state.selected;
    var needsBody = ep && /^(PUT|POST|PATCH)$/i.test(ep.Method);
    els.bodyField.hidden = !needsBody;
    if (!needsBody) {
      els.requestBody.value = "";
      els.bodyHint.textContent = "";
      return;
    }

    els.bodyHint.textContent = bodyHintText(ep);
    els.bodyHint.className = "hint muted";

    if (!force && els.requestBody.dataset.endpoint === (ep.Method + " " + ep.Path) && els.requestBody.value.trim()) {
      if (els.chkFillContext.checked) {
        mergeContextIntoBody();
      }
      return;
    }

    var ctx = contextForEndpoint(ep);
    var obj = buildExampleBody(ep, ctx, els.chkFillContext.checked);
    els.requestBody.value = JSON.stringify(obj, null, 2);
    els.requestBody.dataset.endpoint = ep.Method + " " + ep.Path;
  }

  function mergeContextIntoBody() {
    var ep = state.selected;
    if (!ep || !ep.BodyParams || ep.BodyParams.length === 0) return;
    if (!els.chkFillContext.checked) return;

    var obj;
    try {
      obj = JSON.parse(els.requestBody.value || "{}");
      if (!obj || typeof obj !== "object" || Array.isArray(obj)) {
        obj = {};
      }
    } catch (e) {
      obj = buildExampleBody(ep, contextForEndpoint(ep), true);
    }

    var ctx = contextForEndpoint(ep);
    var changed = false;
    ep.BodyParams.forEach(function (param) {
      if (!isContextParam(param.Name)) return;
      var raw = contextValue(ctx, param.Name);
      if (!raw) return;
      var value = paramType(param) === "number" ? Number(raw) : raw;
      if (paramType(param) === "number" && Number.isNaN(value)) return;
      if (obj[param.Name] !== value) {
        obj[param.Name] = value;
        changed = true;
      }
    });
    if (changed || !els.requestBody.value.trim()) {
      els.requestBody.value = JSON.stringify(obj, null, 2);
    }
  }

  function fillSelect(select, placeholder, items, labelFn) {
    select.innerHTML = "";
    var opt0 = document.createElement("option");
    opt0.value = "";
    opt0.textContent = placeholder;
    select.appendChild(opt0);
    items.forEach(function (item, i) {
      var opt = document.createElement("option");
      opt.value = String(i);
      opt.textContent = labelFn(item);
      select.appendChild(opt);
    });
    select.disabled = items.length === 0;
  }

  async function loadProjects() {
    setStatus("Projekte werden geladen …");
    var result = await apiFetch("/api/projects/list");
    if (!result.response.ok) {
      state.projects = [];
      fillSelect(els.selProject, "— keine Projekte —", [], function () { return ""; });
      fillSelect(els.selPlc, "— zuerst Projekt wählen —", [], function () { return ""; });
      fillSelect(els.selHmi, "— zuerst Projekt wählen —", [], function () { return ""; });
      els.selPlc.disabled = true;
      els.selHmi.disabled = true;
      setStatus(result.text || ("Fehler " + result.response.status), "error");
      return;
    }
    state.projects = parseJsonOrThrow(result.text, result.text);
    fillSelect(els.selProject, "— Projekt wählen —", state.projects, function (p) {
      var kind = p.Kind ? (" · " + p.Kind) : "";
      return p.Name + kind + "  (PID " + p.ProcessId + ")";
    });
    fillSelect(els.selPlc, "— zuerst Projekt wählen —", [], function () { return ""; });
    fillSelect(els.selHmi, "— zuerst Projekt wählen —", [], function () { return ""; });
    els.selPlc.disabled = true;
    els.selHmi.disabled = true;
    setStatus(state.projects.length + " Projekt(e) gefunden.", "ok");
    applyContextToParams();
    updateRequestPreview();
  }

  async function loadDevices() {
    var project = selectedProject();
    if (!project) {
      state.plcs = [];
      state.hmis = [];
      fillSelect(els.selPlc, "— zuerst Projekt wählen —", [], function () { return ""; });
      fillSelect(els.selHmi, "— zuerst Projekt wählen —", [], function () { return ""; });
      els.selPlc.disabled = true;
      els.selHmi.disabled = true;
      applyContextToParams();
      updateRequestPreview();
      return;
    }

    var qs = "?processId=" + encodeURIComponent(project.ProcessId)
      + "&projectName=" + encodeURIComponent(project.Name)
      + (project.Kind ? "&kind=" + encodeURIComponent(project.Kind) : "");
    setStatus("PLC- und HMI-Geräte werden geladen …");

    var plcResult = await apiFetch("/api/plc/devices/list" + qs);
    var hmiResult = await apiFetch("/api/hmi/devices/list" + qs);

    var messages = [];
    if (plcResult.response.ok) {
      state.plcs = parseJsonOrThrow(plcResult.text, plcResult.text);
      fillSelect(els.selPlc, "— PLC wählen (optional) —", state.plcs, function (d) {
        return d.DeviceName + " - " + d.DeviceItemName;
      });
      messages.push(state.plcs.length + " PLC");
    } else {
      state.plcs = [];
      fillSelect(els.selPlc, "— keine PLC —", [], function () { return ""; });
      messages.push("PLC: " + (plcResult.text || plcResult.response.status));
    }

    if (hmiResult.response.ok) {
      state.hmis = parseJsonOrThrow(hmiResult.text, hmiResult.text);
      fillSelect(els.selHmi, "— HMI wählen (optional) —", state.hmis, function (d) {
        return d.DeviceName + " - " + d.DeviceItemName;
      });
      messages.push(state.hmis.length + " HMI");
    } else {
      state.hmis = [];
      fillSelect(els.selHmi, "— keine HMI —", [], function () { return ""; });
      messages.push("HMI: " + (hmiResult.text || hmiResult.response.status));
    }

    var ok = plcResult.response.ok || hmiResult.response.ok;
    setStatus(messages.join(" · "), ok ? "ok" : "error");
    applyContextToParams();
    updateRequestPreview();
  }

  async function loadHelp() {
    var result = await apiFetch("/help", { needsKey: false });
    if (!result.response.ok) {
      throw new Error(result.text || ("Hilfe konnte nicht geladen werden (" + result.response.status + ")."));
    }
    state.help = parseJsonOrThrow(result.text);
    state.endpoints = (state.help.Endpoints || []).slice().sort(function (a, b) {
      var ga = (a.Group || "").localeCompare(b.Group || "");
      if (ga !== 0) return ga;
      return (a.Path || "").localeCompare(b.Path || "");
    });

    els.selEndpoint.innerHTML = "";
    var placeholder = document.createElement("option");
    placeholder.value = "";
    placeholder.textContent = "— Endpunkt wählen —";
    els.selEndpoint.appendChild(placeholder);

    var currentGroup = null;
    state.endpoints.forEach(function (ep, i) {
      if (ep.Group !== currentGroup) {
        currentGroup = ep.Group;
        var group = document.createElement("optgroup");
        group.label = currentGroup || "Sonstiges";
        els.selEndpoint.appendChild(group);
      }
      var opt = document.createElement("option");
      opt.value = String(i);
      opt.textContent = ep.Method + "  " + ep.Path;
      els.selEndpoint.lastChild.appendChild(opt);
    });
  }

  function renderQueryParams() {
    var ep = state.selected;
    els.queryParams.innerHTML = "";
    state.paramValues = {};

    if (!ep) {
      els.queryParams.innerHTML = "<p class=\"empty\">Endpunkt wählen, um Parameter zu sehen.</p>";
      return;
    }

    var queries = ep.Query || [];
    if (queries.length === 0) {
      els.queryParams.innerHTML = "<p class=\"empty\">Keine Query-Parameter.</p>";
      return;
    }

    var ctx = els.chkFillContext.checked ? contextForEndpoint(ep) : {};
    queries.forEach(function (q) {
      var name = q.Name;
      var wrap = document.createElement("label");
      wrap.className = "field";
      var initial = isContextParam(name) ? (contextValue(ctx, name) || "") : "";
      if (initial) {
        wrap.classList.add("from-context");
      }

      var title = document.createElement("span");
      title.textContent = name;
      var input = document.createElement("input");
      input.type = "text";
      input.dataset.param = name;
      input.placeholder = shortPlaceholder(name, q.Description);
      input.value = initial;
      state.paramValues[name] = input.value;
      input.addEventListener("input", function () {
        state.paramValues[name] = input.value;
        updateRequestPreview();
      });
      wrap.appendChild(title);
      wrap.appendChild(input);
      if (q.Description) {
        var hint = document.createElement("span");
        hint.className = "hint muted";
        hint.textContent = q.Description;
        wrap.appendChild(hint);
      }
      els.queryParams.appendChild(wrap);
    });
  }

  function applyContextToParams() {
    if (!state.selected || !els.chkFillContext.checked) {
      updateRequestPreview();
      return;
    }
    var ctx = contextForEndpoint(state.selected);
    Array.prototype.forEach.call(els.queryParams.querySelectorAll("input[data-param]"), function (input) {
      var name = input.dataset.param;
      if (!isContextParam(name)) return;
      var value = contextValue(ctx, name);
      if (value) {
        input.value = value;
        state.paramValues[name] = value;
        if (input.parentElement) input.parentElement.classList.add("from-context");
      } else if (input.parentElement) {
        input.parentElement.classList.remove("from-context");
      }
    });
    mergeContextIntoBody();
    updateRequestPreview();
  }

  function buildUrl() {
    var ep = state.selected;
    if (!ep) return "";
    var params = new URLSearchParams();
    Object.keys(state.paramValues).forEach(function (name) {
      var value = state.paramValues[name];
      if (value == null || String(value).trim() === "") return;
      if (name.toLowerCase() === "path" && String(value).indexOf(",") >= 0) {
        String(value).split(",").map(function (v) { return v.trim(); }).filter(Boolean)
          .forEach(function (v) { params.append("path", v); });
      } else {
        params.append(name, value);
      }
    });
    var qs = params.toString();
    return ep.Path + (qs ? "?" + qs : "");
  }

  function updateRequestPreview() {
    var ep = state.selected;
    els.requestUrl.textContent = ep ? buildUrl() : "";
    els.btnSend.disabled = !ep;
  }

  function onEndpointChange() {
    var idx = els.selEndpoint.value;
    state.selected = idx === "" ? null : state.endpoints[Number(idx)];
    var ep = state.selected;

    els.methodBadge.textContent = ep ? ep.Method : "—";
    els.methodBadge.className = "method-badge" + (ep ? " " + ep.Method : "");
    els.endpointSummary.textContent = ep && ep.Summary ? ep.Summary : "";

    renderQueryParams();
    writeBodyTemplate(true);
    updateRequestPreview();
  }

  function formatResponse(text) {
    if (!text) return "(leerer Body)";
    try {
      return JSON.stringify(JSON.parse(text), null, 2);
    } catch (e) {
      return text;
    }
  }

  async function sendRequest() {
    var ep = state.selected;
    if (!ep) return;

    var url = buildUrl();
    els.btnSend.disabled = true;
    els.responseMeta.textContent = "Wird gesendet …";
    els.responseMeta.className = "muted";
    els.responseBody.textContent = "";

    try {
      var options = {
        method: ep.Method,
        needsKey: ep.ApiKeyRequired !== false
      };
      if (/^(PUT|POST|PATCH)$/i.test(ep.Method)) {
        options.body = els.requestBody.value;
      }
      var started = performance.now();
      var result = await apiFetch(url, options);
      var ms = Math.round(performance.now() - started);
      var ok = result.response.ok;
      els.responseMeta.textContent = result.response.status + " " + result.response.statusText
        + " · " + ms + " ms · " + (result.response.headers.get("Content-Type") || "text");
      els.responseMeta.className = ok ? "ok" : "err";
      els.responseBody.textContent = formatResponse(result.text);
    } catch (err) {
      els.responseMeta.textContent = "Fehler";
      els.responseMeta.className = "err";
      els.responseBody.textContent = String(err && err.message ? err.message : err);
    } finally {
      els.btnSend.disabled = !state.selected;
    }
  }

  function bind() {
    els.apiKey.value = localStorage.getItem(STORAGE_KEY) || "";
    els.apiKey.addEventListener("change", function () {
      localStorage.setItem(STORAGE_KEY, els.apiKey.value);
    });
    els.apiKey.addEventListener("blur", function () {
      localStorage.setItem(STORAGE_KEY, els.apiKey.value);
    });

    els.btnToggleKey.addEventListener("click", function () {
      var show = els.apiKey.type === "password";
      els.apiKey.type = show ? "text" : "password";
      els.btnToggleKey.textContent = show ? "verbergen" : "zeigen";
    });

    els.btnRefreshContext.addEventListener("click", function () {
      loadProjects().then(function () {
        if (selectedProject()) return loadDevices();
      }).catch(function (err) {
        setStatus(String(err && err.message ? err.message : err), "error");
      });
    });

    els.selProject.addEventListener("change", function () {
      loadDevices().catch(function (err) {
        setStatus(String(err && err.message ? err.message : err), "error");
      });
    });

    els.selPlc.addEventListener("change", applyContextToParams);
    els.selHmi.addEventListener("change", applyContextToParams);
    els.chkFillContext.addEventListener("change", applyContextToParams);
    els.selEndpoint.addEventListener("change", onEndpointChange);
    els.btnSend.addEventListener("click", sendRequest);
  }

  async function init() {
    bind();
    try {
      await loadHelp();
      if (apiKey()) {
        await loadProjects();
      } else {
        setStatus("API-Key eingeben und „Aktualisieren“ wählen.", "muted");
      }
    } catch (err) {
      setStatus(String(err && err.message ? err.message : err), "error");
    }
  }

  init();
})();
