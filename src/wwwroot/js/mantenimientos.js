"use strict";
/*
 * mantenimientos.js — Página /Mantenimientos/ (solo visualización).
 *
 * Muestra TODOS los mantenimientos de TODOS los equipos en una lista
 * única, sin botones de acción (no se puede editar, cancelar ni cerrar
 * desde aquí; eso se hace desde la ficha de cada equipo en /equipos/).
 *
 * Endpoint: GET /api/maintenance → lista de MantenimientoConEquipo
 *   { id, equipoId, nombreEquipo, estadoEquipo, tipo, fechaInicio,
 *     fechaFin, estado, duracionSegundos, copiaOutlook, mantenimiento,
 *     reparacionWindows, apagarEquipo, mensaje, dias, vigenciaFin, … }
 */

// ============ Estado ============
const state = {
  refreshing: false,
  mantenimientos: [],
};

// ============ Elementos del DOM ============
const elements = {
  refreshButton: document.querySelector("#refreshButton"),
  lastUpdate: document.querySelector("#lastUpdate"),
  alertBox: document.querySelector("#alertBox"),
  totalCount: document.querySelector("#totalCount"),
  pendingCount: document.querySelector("#pendingCount"),
  diarioCount: document.querySelector("#diarioCount"),
  doneCount: document.querySelector("#doneCount"),
  searchInput: document.querySelector("#searchInput"),
  estadoFilter: document.querySelector("#estadoFilter"),
  equipoFilter: document.querySelector("#equipoFilter"),
  maintenanceList: document.querySelector("#maintenanceList"),
  emptyState: document.querySelector("#emptyState"),
};

// ============ Petición JSON ============
async function requestJson(url) {
  const response = await fetch(url, {
    cache: "no-store",
    headers: { Accept: "application/json" },
  });
  let payload;
  try {
    payload = await response.json();
  } catch {
    payload = null;
  }
  if (!response.ok) {
    throw new Error(payload?.error || `Error HTTP ${response.status}`);
  }
  return payload;
}

// ============ Refresco principal ============
async function refreshMaintenance(options = {}) {
  if (state.refreshing) return;
  const silencioso = options.silent === true;
  state.refreshing = true;
  if (!silencioso) {
    elements.refreshButton.disabled = true;
    hideAlert();
  }

  try {
    const data = await requestJson("/api/maintenance");
    state.mantenimientos = Array.isArray(data) ? data : [];
    populateEquipoFilter();
    renderMaintenance();
    elements.lastUpdate.textContent = new Date().toLocaleTimeString("es-ES");
  } catch (error) {
    if (!silencioso) showAlert(`No se han podido cargar los mantenimientos: ${error.message}`);
  } finally {
    state.refreshing = false;
    if (!silencioso) elements.refreshButton.disabled = false;
  }
}

// ============ Filtros ============
function populateEquipoFilter() {
  const current = elements.equipoFilter.value;
  const nombres = [...new Set(state.mantenimientos.map((m) => m.nombreEquipo).filter(Boolean))].sort();
  elements.equipoFilter.innerHTML =
    `<option value="all">Todos los equipos</option>` +
    nombres.map((n) => `<option value="${escapeHtml(n)}">${escapeHtml(n)}</option>`).join("");
  // Restaurar selección si sigue existiendo.
  if (current && [...elements.equipoFilter.options].some((o) => o.value === current)) {
    elements.equipoFilter.value = current;
  }
}

function getFiltered() {
  const q = elements.searchInput.value.trim().toLocaleLowerCase("es-ES");
  const estado = elements.estadoFilter.value;
  const equipo = elements.equipoFilter.value;

  return state.mantenimientos.filter((m) => {
    if (estado !== "all") {
      const mEstado = String(m.estado || "").toLocaleLowerCase("es-ES");
      if (mEstado !== estado) return false;
    }
    if (equipo !== "all" && m.nombreEquipo !== equipo) return false;
    if (q) {
      const haystack = `${m.nombreEquipo} ${m.mensaje} ${m.estado}`.toLocaleLowerCase("es-ES");
      if (!haystack.includes(q)) return false;
    }
    return true;
  });
}

// ============ Render ============
function renderMaintenance() {
  const filtered = getFiltered();
  updateSummary(state.mantenimientos);

  if (filtered.length === 0) {
    elements.maintenanceList.innerHTML = "";
    elements.emptyState.classList.remove("hidden");
    return;
  }
  elements.emptyState.classList.add("hidden");

  elements.maintenanceList.innerHTML = filtered.map(renderCard).join("");
}

function renderCard(item) {
  const estado = String(item.estado || "").trim();
  const estadoNorm = estado.toLocaleLowerCase("es-ES");
  const esDiario = estadoNorm === "diario";

  const cardClass =
    estadoNorm === "programado" ? "is-planned"
    : estadoNorm === "diario" ? "is-diario"
    : estadoNorm === "ejecutando" || estadoNorm === "enproceso" ? "is-running"
    : estadoNorm === "cerrado" ? "is-closed"
    : estadoNorm === "cancelado" ? "is-cancelled"
    : "is-done";

  // Chips de acción activas.
  const acciones = [
    [item.copiaOutlook, "Copia Outlook", false],
    [item.mantenimiento, "Mantenimiento", false],
    [item.reparacionWindows, "Reparación", false],
    [item.apagarEquipo, "Apagar equipo", true],
  ].filter(([active]) => active);
  const accionesHtml = acciones.length
    ? acciones.map(([, label, danger]) => `<span class="action-chip${danger ? " danger" : ""}">${escapeHtml(label)}</span>`).join("")
    : `<span class="maintenance-no-actions">Sin acciones de mantenimiento</span>`;

  // Fechas según tipo.
  const duracion = Number(item.duracionSegundos || 0);
  const duracionTexto = duracion > 0 ? ` <span>· ${escapeHtml(formatDuration(duracion))}</span>` : "";
  const esAbierto = ["programado", "ejecutando", "diario", "enproceso"].includes(estadoNorm);

  let fechasHtml;
  if (esDiario) {
    fechasHtml = renderDiarioSchedule(item);
  } else if (esAbierto) {
    fechasHtml = `<span>Desde</span> ${escapeHtml(formatDateTime(item.fechaInicio))}`;
  } else {
    fechasHtml = `${escapeHtml(formatDateTime(item.fechaInicio))} <span>hasta</span> ${escapeHtml(formatDateTime(item.fechaFin))}${duracionTexto}`;
  }

  // Pill del estado del equipo (Online/Offline).
  const equipoEstado = String(item.estadoEquipo || "").trim();
  const equipoPillClass = equipoEstado.toLocaleLowerCase("es-ES") === "online" ? "online" : "offline";

  return `
    <article class="maintenance-card ${cardClass}">
      <div class="maintenance-header">
        <div>
          <span class="status-pill ${maintenancePill(estado)}">${escapeHtml(estado || "Sin estado")}</span>
          <span class="maintenance-when">${fechasHtml}</span>
        </div>
        <div class="maintenance-buttons">
          <span class="status-pill ${equipoPillClass}">${escapeHtml(equipoEstado || "—")}</span>
        </div>
      </div>
      <div class="maintenance-equipment">
        <strong>${escapeHtml(item.nombreEquipo || "Equipo desconocido")}</strong>
      </div>
      <div class="maintenance-actions">${accionesHtml}</div>
      ${item.mensaje ? `<p class="maintenance-message">${escapeHtml(item.mensaje)}</p>` : ""}
      ${item.detalleTecnico ? `<p class="maintenance-detail">${escapeHtml(item.detalleTecnico)}</p>` : ""}
    </article>`;
}

function updateSummary(all) {
  elements.totalCount.textContent = String(all.length);
  elements.pendingCount.textContent = String(
    all.filter((m) => ["programado", "ejecutando", "enproceso"].includes(String(m.estado || "").toLocaleLowerCase("es-ES"))).length
  );
  elements.diarioCount.textContent = String(
    all.filter((m) => String(m.estado || "").toLocaleLowerCase("es-ES") === "diario").length
  );
  elements.doneCount.textContent = String(
    all.filter((m) => ["completado", "cerrado", "finalizado", "ok"].includes(String(m.estado || "").toLocaleLowerCase("es-ES"))).length
  );
}

// ============ Diario (texto de recurrencia) ============
function renderDiarioSchedule(item) {
  const dias = Array.isArray(item.dias) ? item.dias : [];
  const nombres = ["dom", "lun", "mar", "mié", "jue", "vie", "sáb"];
  const textoDias = dias.length
    ? dias.slice().sort().map((d) => nombres[Number(d)] ?? d).join(", ")
    : "sin días";
  const fecha = parseDate(item.fechaInicio);
  const hora = fecha
    ? new Intl.DateTimeFormat("es-ES", { hour: "2-digit", minute: "2-digit" }).format(fecha)
    : "—";
  let html = `Cada <strong>${escapeHtml(textoDias)}</strong> <span>a las</span> ${escapeHtml(hora)}`;
  if (item.vigenciaFin) html += ` <span>hasta</span> ${escapeHtml(formatDateOnly(item.vigenciaFin))}`;
  else html += ` <span>· sin fin</span>`;
  return html;
}

// ============ Utilidades ============
function maintenancePill(estado) {
  const value = String(estado || "").toLocaleLowerCase("es-ES");
  if (value === "programado") return "planned";
  if (value === "diario") return "daily";
  if (value === "ejecutando" || value === "enproceso") return "running";
  if (value === "cerrado") return "closed";
  if (value === "cancelado") return "cancelled";
  if (["completado", "finalizado", "ok"].includes(value)) return "online";
  return "offline";
}

function parseDate(value) {
  if (!value) return null;
  const d = new Date(value);
  return isNaN(d.getTime()) ? null : d;
}

function formatDateTime(value) {
  const d = parseDate(value);
  return d
    ? new Intl.DateTimeFormat("es-ES", { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(d)
    : "—";
}

function formatDateOnly(value) {
  const d = parseDate(value);
  return d
    ? new Intl.DateTimeFormat("es-ES", { day: "2-digit", month: "short", year: "numeric" }).format(d)
    : "—";
}

function formatDuration(seconds) {
  if (!seconds || seconds <= 0) return "";
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  if (m === 0) return `${s}s`;
  return s === 0 ? `${m} min` : `${m} min ${s}s`;
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function showAlert(message) {
  elements.alertBox.textContent = message;
  elements.alertBox.classList.remove("hidden");
}

function hideAlert() {
  elements.alertBox.classList.add("hidden");
}

// ============ Eventos e inicialización ============
elements.refreshButton.addEventListener("click", () => refreshMaintenance());
elements.searchInput.addEventListener("input", renderMaintenance);
elements.estadoFilter.addEventListener("change", renderMaintenance);
elements.equipoFilter.addEventListener("change", renderMaintenance);

// ============ Tiempo real (SignalR) ============
// Cuando el servidor avisa de un cambio de estado (Programado → Ejecutando,
// → Completado/EnProceso/Error/Cancelado…), la lista se recarga sola. Varios
// avisos seguidos se agrupan en una única recarga (pequeño antirrebote).
let refreshTimer = null;
function programarRefresco() {
  window.clearTimeout(refreshTimer);
  refreshTimer = window.setTimeout(refreshMaintenance, 300);
}
document.addEventListener("realtime:mantenimiento", programarRefresco);
document.addEventListener("realtime:reconectado", programarRefresco);

// ============ Refresco periódico de seguridad ============
// Cada 10 segundos repasa el estado de todos los mantenimientos, por si se
// pierde algún aviso del socket (red, pestaña dormida, etc.).
window.setInterval(() => refreshMaintenance({ silent: true }), 10000);

// Carga inicial de todos los mantenimientos.
refreshMaintenance();
