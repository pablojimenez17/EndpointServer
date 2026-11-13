"use strict";

/*
 * app.js — Lógica del panel principal (dashboard) cargado en "/".
 *
 * Funcionamiento:
 *  1. Consulta /api/status (estado del servidor y BD) y /api/equipos
 *     (equipos registrados) y los pinta en tarjetas de resumen.
 *  2. Muestra los equipos en una tabla con búsqueda libre y filtro
 *     online/offline; se refresca solo cada 15 segundos.
 *  3. El botón "Ver detalles" de cada fila abre un diálogo modal con
 *     pestañas: General, Hardware, Discos, Software (carga perezosa
 *     vía /api/equipos/{id}/software) y Mantenimiento.
 *
 * Nota: esta es la versión original del diálogo de detalles; la página
 * /equipos/ usa una evolución de este mismo patrón en equipos.js.
 */

// ============ Estado en memoria y referencias al DOM ============

// Estado global de la página (equipos cargados, flags de refresco, software del equipo seleccionado).
const state = {
  equipos: [],
  refreshing: false,
  refreshTimer: null,
  selectedEquipoId: null,
  selectedResponsable: null,
  software: [],
  softwareLoadedFor: null
};

// Acceso directo a los elementos HTML que usa el script (IDs de index.html).
const elements = {
  apiIndicator: document.querySelector("#apiIndicator"),
  apiState: document.querySelector("#apiState"),
  serverVersion: document.querySelector("#serverVersion"),
  databaseState: document.querySelector("#databaseState"),
  totalCount: document.querySelector("#totalCount"),
  onlineCount: document.querySelector("#onlineCount"),
  offlineCount: document.querySelector("#offlineCount"),
  lastUpdate: document.querySelector("#lastUpdate"),
  refreshButton: document.querySelector("#refreshButton"),
  searchInput: document.querySelector("#searchInput"),
  stateFilter: document.querySelector("#stateFilter"),
  tableBody: document.querySelector("#equipmentTableBody"),
  emptyState: document.querySelector("#emptyState"),
  alertBox: document.querySelector("#alertBox"),
  detailsDialog: document.querySelector("#detailsDialog"),
  closeDialog: document.querySelector("#closeDialog"),
  detailName: document.querySelector("#detailName"),
  detailStatus: document.querySelector("#detailStatus"),
  detailLoading: document.querySelector("#detailLoading"),
  detailContent: document.querySelector("#detailContent"),
  detailList: document.querySelector("#detailList"),
  inventoryList: document.querySelector("#inventoryList"),
  diskList: document.querySelector("#diskList"),
  diskCount: document.querySelector("#diskCount"),
  maintenanceContent: document.querySelector("#maintenanceContent"),
  detailTabs: [...document.querySelectorAll(".detail-tab")],
  detailPanels: [...document.querySelectorAll(".detail-tab-panel")],
  softwareSearch: document.querySelector("#softwareSearch"),
  softwareCount: document.querySelector("#softwareCount"),
  softwareLoading: document.querySelector("#softwareLoading"),
  softwareList: document.querySelector("#softwareList")
};

// ============ Comunicación con la API ============

/*
 * Petición GET JSON genérica: desactiva la caché del navegador, parsea la
 * respuesta y lanza Error con el mensaje del servidor si el estado no es 2xx.
 */
async function requestJson(url) {
  const response = await fetch(url, {
    cache: "no-store",
    headers: { "Accept": "application/json" }
  });

  let payload;
  try {
    payload = await response.json();
  } catch {
    payload = null;
  }

  if (!response.ok) {
    const message = payload?.error || payload?.title || `Error HTTP ${response.status}`;
    throw new Error(message);
  }

  return payload;
}

// ============ Panel principal: refresco y tarjetas ============

/*
 * Refresco completo: descarga estado del servidor y equipos en paralelo,
 * redibuja tarjetas y tabla, y actualiza la hora de la última actualización.
 * Usa state.refreshing para evitar peticiones superpuestas.
 */
async function refreshDashboard() {
  if (state.refreshing) return;

  state.refreshing = true;
  elements.refreshButton.disabled = true;
  elements.refreshButton.textContent = "Actualizando";
  hideAlert();

  try {
    const [status, equipos] = await Promise.all([
      requestJson("/api/status"),
      requestJson("/api/equipos")
    ]);

    state.equipos = Array.isArray(equipos) ? equipos : [];
    renderStatus(status);
    renderSummary();
    renderTable();
    elements.lastUpdate.textContent = new Intl.DateTimeFormat("es-ES", {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit"
    }).format(new Date());
  } catch (error) {
    markServerError();
    showAlert(`No se ha podido actualizar el panel: ${error.message}`);
  } finally {
    state.refreshing = false;
    elements.refreshButton.disabled = false;
    elements.refreshButton.textContent = "Actualizar";
  }
}

// Pinta el indicador del sidebar (API + BD) y la versión del servidor.
function renderStatus(status) {
  const apiOk = status?.api === "OK";
  const dbOk = status?.database === "OK";

  elements.apiIndicator.className = `service-dot ${apiOk && dbOk ? "ok" : "error"}`;
  elements.apiState.textContent = apiOk && dbOk ? "Servidor operativo" : "Servidor con incidencias";
  elements.serverVersion.textContent = `Versión ${status?.version || "—"}`;
  elements.databaseState.textContent = status?.database || "—";
  elements.databaseState.style.color = dbOk ? "var(--success)" : "var(--danger)";
}

// Marca el indicador en rojo cuando falla la petición al servidor.
function markServerError() {
  elements.apiIndicator.className = "service-dot error";
  elements.apiState.textContent = "Error de conexión";
  elements.databaseState.textContent = "ERROR";
  elements.databaseState.style.color = "var(--danger)";
}

// Actualiza las tarjetas de resumen: total, online y offline.
function renderSummary() {
  const online = state.equipos.filter(isOnline).length;
  elements.totalCount.textContent = state.equipos.length;
  elements.onlineCount.textContent = online;
  elements.offlineCount.textContent = state.equipos.length - online;
}

// ============ Tabla de equipos ============

/*
 * Filtra por texto de búsqueda (nombre, usuario, dominio, SO, IP, versión)
 * y por estado (todo/online/offline), y reconstruye las filas de la tabla.
 */
function renderTable() {
  const query = elements.searchInput.value.trim().toLocaleLowerCase("es-ES");
  const filter = elements.stateFilter.value;

  const filtered = state.equipos.filter((equipo) => {
    const online = isOnline(equipo);
    const stateMatches = filter === "all" || (filter === "online" && online) || (filter === "offline" && !online);
    const searchable = [
      equipo.nombreEquipo,
      equipo.responsable,
      equipo.usuario,
      equipo.dominio,
      equipo.sistemaOperativo,
      equipo.direccionIP,
      equipo.versionAgente
    ].filter(Boolean).join(" ").toLocaleLowerCase("es-ES");

    return stateMatches && (!query || searchable.includes(query));
  });

  elements.tableBody.innerHTML = "";
  elements.emptyState.classList.toggle("hidden", filtered.length !== 0);
  document.querySelector(".table-wrap").classList.toggle("hidden", filtered.length === 0);

  for (const equipo of filtered) {
    const row = document.createElement("tr");
    const online = isOnline(equipo);

    row.innerHTML = `
      <td><span class="status-pill ${online ? "online" : "offline"}">${online ? "Online" : "Offline"}</span></td>
      <td>
        <span class="computer-name">${escapeHtml(equipo.nombreEquipo || "Sin nombre")}</span>
        <span class="computer-domain">${escapeHtml(equipo.dominio || "Sin dominio")}</span>
      </td>
      <td>${escapeHtml(equipo.responsable || "—")}</td>
      <td title="${escapeHtml(equipo.sistemaOperativo || "")}">${escapeHtml(shortOperatingSystem(equipo.sistemaOperativo))}</td>
      <td>${escapeHtml(equipo.direccionIP || "—")}</td>
      <td>${escapeHtml(equipo.versionAgente || "—")}</td>
      <td title="${escapeHtml(formatFullDate(equipo.ultimoHeartbeat))}">${escapeHtml(formatRelativeDate(equipo.ultimoHeartbeat))}</td>
      <td><button class="details-button" type="button">Ver detalles</button></td>
    `;

    row.querySelector(".details-button").addEventListener("click", () => showDetails(equipo));
    elements.tableBody.appendChild(row);
  }
}

// ============ Diálogo de detalles del equipo ============

/*
 * Abre el diálogo modal, muestra el indicador de carga y descarga el
 * detalle completo del equipo (/api/equipos/{id}/detalle: equipo +
 * inventario + discos + último mantenimiento).
 */
async function showDetails(equipo) {
  state.selectedEquipoId = equipo.id;
  state.selectedResponsable = equipo.responsable || null;
  state.software = [];
  state.softwareLoadedFor = null;
  elements.softwareSearch.value = "";
  elements.softwareCount.textContent = "Sin cargar";
  elements.softwareList.innerHTML = "";
  activateDetailTab("general");

  const online = isOnline(equipo);
  elements.detailName.textContent = equipo.nombreEquipo || "Equipo sin nombre";
  elements.detailStatus.innerHTML = `<span class="status-pill ${online ? "online" : "offline"}">${online ? "Online" : "Offline"}</span>`;
  elements.detailLoading.classList.remove("hidden");
  elements.detailContent.classList.add("hidden");
  elements.detailsDialog.showModal();

  try {
    const detalle = await requestJson(`/api/equipos/${equipo.id}/detalle`);
    renderEquipmentDetails(detalle);
    elements.detailLoading.classList.add("hidden");
    elements.detailContent.classList.remove("hidden");
  } catch (error) {
    elements.detailLoading.textContent = `No se ha podido cargar el detalle: ${error.message}`;
  }
}

// Rellena las pestañas General/Hardware/Discos/Mantenimiento con el detalle.
function renderEquipmentDetails(detalle) {
  const equipo = detalle?.equipo || {};
  const inventario = detalle?.inventario;
  const discos = Array.isArray(detalle?.discos) ? detalle.discos : [];
  const mantenimiento = detalle?.ultimoMantenimiento;

  elements.detailList.innerHTML = renderDetailItems([
    ["Responsable", state.selectedResponsable],
    ["Usuario", equipo.usuario],
    ["Dominio", equipo.dominio],
    ["Sistema operativo", equipo.sistemaOperativo],
    ["Versión del SO", equipo.versionSO],
    ["Dirección IP", equipo.direccionIP],
    ["Versión del agente", equipo.versionAgente],
    ["Fecha de registro", formatFullDate(equipo.fechaRegistro)],
    ["Último heartbeat", formatFullDate(equipo.ultimoHeartbeat)],
    ["GUID", equipo.guid]
  ]);

  if (inventario) {
    elements.inventoryList.innerHTML = renderDetailItems([
      ["Fabricante", inventario.fabricante],
      ["Modelo", inventario.modelo],
      ["Número de serie", inventario.numeroSerie],
      ["Procesador", inventario.procesador],
      ["Núcleos", inventario.nucleos],
      ["Memoria RAM", `${formatNumber(inventario.memoriaRamGB, 2)} GB`],
      ["Arquitectura", inventario.arquitectura],
      ["Windows detectado", inventario.versionWindows],
      ["Fecha de instalación", formatFullDate(inventario.fechaInstalacion)],
      ["IP principal", inventario.ipPrincipal],
      ["MAC principal", inventario.macPrincipal],
      ["Último inventario", formatFullDate(inventario.fechaInventario)]
    ]);
  } else {
    elements.inventoryList.innerHTML = `<div class="detail-empty">Este equipo todavía no ha enviado inventario.</div>`;
  }

  elements.diskCount.textContent = discos.length === 1 ? "1 unidad" : `${discos.length} unidades`;
  elements.diskList.innerHTML = discos.length
    ? discos.map(renderDisk).join("")
    : `<div class="detail-empty">No hay información de discos disponible.</div>`;

  elements.maintenanceContent.innerHTML = mantenimiento
    ? renderMaintenance(mantenimiento)
    : `<div class="detail-empty">Este equipo todavía no tiene mantenimientos registrados.</div>`;
}

// ============ Pestaña de software (carga perezosa) ============

/*
 * Descarga el software del equipo la primera vez que se abre su pestaña
 * (state.softwareLoadedFor evita repetir la petición al cambiar de pestaña).
 */
async function loadSoftwareForSelectedEquipment() {
  const equipoId = state.selectedEquipoId;
  if (!equipoId || state.softwareLoadedFor === equipoId) return;

  elements.softwareLoading.classList.remove("hidden");
  elements.softwareList.innerHTML = "";

  try {
    const software = await requestJson(`/api/equipos/${equipoId}/software`);
    state.software = Array.isArray(software) ? software : [];
    state.softwareLoadedFor = equipoId;
    renderSoftware();
  } catch (error) {
    elements.softwareList.innerHTML = `<div class="detail-empty">No se ha podido cargar el software: ${escapeHtml(error.message)}</div>`;
    elements.softwareCount.textContent = "Error";
  } finally {
    elements.softwareLoading.classList.add("hidden");
  }
}

// Filtra por búsqueda local y repinta la lista de programas del equipo.
function renderSoftware() {
  const query = elements.softwareSearch.value.trim().toLocaleLowerCase("es-ES");
  const filtered = state.software.filter((item) => {
    const searchable = [item.nombre, item.version, item.fabricante, item.arquitectura]
      .filter(Boolean).join(" ").toLocaleLowerCase("es-ES");
    return !query || searchable.includes(query);
  });

  elements.softwareCount.textContent = `${filtered.length} de ${state.software.length} programas`;
  elements.softwareList.innerHTML = filtered.length
    ? filtered.map(renderSoftwareItem).join("")
    : `<div class="detail-empty">${state.software.length ? "No hay coincidencias con la búsqueda." : "Este equipo todavía no ha enviado inventario de software."}</div>`;
}

// Genera el HTML de una tarjeta de programa (nombre, fabricante, versión…).
function renderSoftwareItem(item) {
  const installDate = item.fechaInstalacion ? formatDateOnly(item.fechaInstalacion) : "—";
  return `
    <article class="software-item">
      <div class="software-main">
        <strong>${escapeHtml(item.nombre || "Sin nombre")}</strong>
        <span>${escapeHtml(item.fabricante || "Fabricante no indicado")}</span>
      </div>
      <div class="software-meta">
        <span><b>Versión</b>${escapeHtml(item.version || "—")}</span>
        <span><b>Arquitectura</b>${escapeHtml(item.arquitectura || "—")}</span>
        <span><b>Instalado</b>${escapeHtml(installDate)}</span>
      </div>
    </article>`;
}

// Activa una pestaña del diálogo (resalta el botón, muestra su panel y
// dispara la carga diferida si es la de software).
function activateDetailTab(tabName) {
  for (const tab of elements.detailTabs) {
    const active = tab.dataset.tab === tabName;
    tab.classList.toggle("active", active);
    tab.setAttribute("aria-selected", active ? "true" : "false");
  }

  for (const panel of elements.detailPanels) {
    const active = panel.dataset.panel === tabName;
    panel.classList.toggle("active", active);
    panel.hidden = !active;
  }

  if (tabName === "software") loadSoftwareForSelectedEquipment();
}

// ============ Pintores auxiliares de HTML ============

// Convierte pares etiqueta/valor en filas de la lista de detalle (dt/dd).
function renderDetailItems(items) {
  return items.map(([label, value]) => `
    <div class="detail-item">
      <dt>${escapeHtml(label)}</dt>
      <dd>${escapeHtml(value === 0 ? "0" : (value || "—"))}</dd>
    </div>
  `).join("");
}

// Tarjeta de disco con barra de uso (calcula usados = total - libre).
function renderDisk(disco) {
  const total = Number(disco.tamanoBytes || 0);
  const free = Number(disco.libreBytes || 0);
  const used = Math.max(0, total - free);
  const usedPercent = total > 0 ? Math.min(100, Math.max(0, (used / total) * 100)) : 0;
  const label = [disco.unidad, disco.etiqueta].filter(Boolean).join(" · ");
  const type = [disco.sistemaArchivos, disco.tipoUnidad].filter(Boolean).join(" · ");

  return `
    <article class="disk-card">
      <div class="disk-card-header">
        <strong>${escapeHtml(label || "Unidad")}</strong>
        <small>${escapeHtml(type || "—")}</small>
      </div>
      <div class="disk-progress" title="${escapeHtml(formatNumber(usedPercent, 1))}% utilizado">
        <span style="width:${usedPercent.toFixed(2)}%"></span>
      </div>
      <div class="disk-card-footer">
        <span>${escapeHtml(formatBytes(used))} utilizados</span>
        <span>${escapeHtml(formatBytes(free))} libres de ${escapeHtml(formatBytes(total))}</span>
      </div>
    </article>
  `;
}

// Tarjeta del último mantenimiento del equipo (estado, flags y mensaje).
function renderMaintenance(item) {
  return `
    <article class="maintenance-card">
      <div class="maintenance-header">
        <div>
          <strong>${escapeHtml(item.estado || "Sin estado")}</strong>
          <div class="detail-section-note">${escapeHtml(formatFullDate(item.fechaFin))} · ${escapeHtml(formatDuration(item.duracionSegundos))}</div>
        </div>
      </div>
      <div class="maintenance-grid">
        ${renderMaintenanceFlag("Copia Outlook", item.copiaOutlook)}
        ${renderMaintenanceFlag("Mantenimiento", item.mantenimiento)}
        ${renderMaintenanceFlag("Reparación", item.reparacionWindows)}
        ${renderMaintenanceFlag("Apagado solicitado", item.apagarEquipo)}
      </div>
      ${item.mensaje ? `<p class="maintenance-message">${escapeHtml(item.mensaje)}</p>` : ""}
    </article>
  `;
}

// Caja Sí/No de una acción del mantenimiento (Copia Outlook, etc.).
function renderMaintenanceFlag(label, active) {
  return `<div class="maintenance-flag"><span>${active ? "Sí" : "No"}</span><strong>${escapeHtml(label)}</strong></div>`;
}

// ============ Formateo de números, tamaños y fechas ============

// Convierte bytes a GB/TB legibles ("512 GB", "1,8 TB").
function formatBytes(value) {
  const bytes = Number(value || 0);
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 GB";
  const gb = bytes / 1073741824;
  if (gb >= 1024) return `${formatNumber(gb / 1024, 2)} TB`;
  return `${formatNumber(gb, gb >= 100 ? 0 : 1)} GB`;
}

// Número en formato español con hasta N decimales ("1.234,5").
function formatNumber(value, decimals = 0) {
  const number = Number(value);
  if (!Number.isFinite(number)) return "—";
  return new Intl.NumberFormat("es-ES", {
    minimumFractionDigits: 0,
    maximumFractionDigits: decimals
  }).format(number);
}

// Segundos → texto ("2 h 15 min", "45 min 3 s", "30 s").
function formatDuration(secondsValue) {
  const total = Math.max(0, Number(secondsValue || 0));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = Math.floor(total % 60);
  if (hours > 0) return `${hours} h ${minutes} min`;
  if (minutes > 0) return `${minutes} min ${seconds} s`;
  return `${seconds} s`;
}

// ============ Utilidades generales ============

// Un equipo se considera Online si el campo estado del servidor dice "online".
function isOnline(equipo) {
  return String(equipo?.estado || "").toLocaleLowerCase("es-ES") === "online";
}

// Abrevia el nombre del SO (quita "Microsoft " y traduce el build 17763).
function shortOperatingSystem(value) {
  if (!value) return "—";
  return value.replace(/^Microsoft\s+/i, "").replace("Windows 10.0.17763", "Windows Server 2019");
}

// Fecha solo con día/mes/año en formato español.
function formatDateOnly(value) {
  const date = parseDate(value);
  if (!date) return "—";
  return new Intl.DateTimeFormat("es-ES", { dateStyle: "medium" }).format(date);
}

// Fecha y hora completas (para tooltips title="…").
function formatFullDate(value) {
  const date = parseDate(value);
  if (!date) return "—";

  return new Intl.DateTimeFormat("es-ES", {
    dateStyle: "medium",
    timeStyle: "medium"
  }).format(date);
}

// Fecha relativa en español ("hace 5 minutos", "mañana") para la tabla.
function formatRelativeDate(value) {
  const date = parseDate(value);
  if (!date) return "—";

  const seconds = Math.round((date.getTime() - Date.now()) / 1000);
  const absolute = Math.abs(seconds);
  const formatter = new Intl.RelativeTimeFormat("es", { numeric: "auto" });

  if (absolute < 60) return formatter.format(seconds, "second");
  if (absolute < 3600) return formatter.format(Math.round(seconds / 60), "minute");
  if (absolute < 86400) return formatter.format(Math.round(seconds / 3600), "hour");
  return formatter.format(Math.round(seconds / 86400), "day");
}

// Convierte un valor ISO a Date; devuelve null si no es interpretable.
function parseDate(value) {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

// Escapa HTML para insertar datos de la BD en el DOM sin riesgo de XSS.
function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

// Muestra/oculta la caja de alerta superior de la página.
function showAlert(message) {
  elements.alertBox.textContent = message;
  elements.alertBox.classList.remove("hidden");
}

function hideAlert() {
  elements.alertBox.classList.add("hidden");
  elements.alertBox.textContent = "";
}

// ============ Eventos e inicialización ============

// Refresco manual, búsqueda y filtro de estado.
elements.refreshButton.addEventListener("click", refreshDashboard);
elements.searchInput.addEventListener("input", renderTable);
elements.stateFilter.addEventListener("change", renderTable);
// Cambio de pestaña en el diálogo de detalles.
for (const tab of elements.detailTabs) {
  tab.addEventListener("click", () => activateDetailTab(tab.dataset.tab));
}
// Búsqueda local dentro del software del equipo seleccionado.
elements.softwareSearch.addEventListener("input", renderSoftware);
// Cierre del diálogo: botón X o clic fuera del contenido.
elements.closeDialog.addEventListener("click", () => elements.detailsDialog.close());
elements.detailsDialog.addEventListener("click", (event) => {
  if (event.target === elements.detailsDialog) elements.detailsDialog.close();
});

// Primera carga + auto-refresco cada 15 segundos (como el sidebar).
refreshDashboard();
state.refreshTimer = window.setInterval(refreshDashboard, 15000);
