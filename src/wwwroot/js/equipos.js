"use strict";

/*
 * equipos.js — Lógica de la página /equipos/ (inventario de hardware).
 *
 * Funcionamiento:
 *  1. GET /api/inventario → tabla de equipos con fabricante, serie, CPU,
 *     RAM, SO, IP y fecha del último inventario (filtro por texto y por
 *     con/sin inventario).
 *  2. Botón "Ver ficha" → diálogo modal con 5 pestañas:
 *       · General    → /api/equipos/{id}/detalle (datos básicos)
 *       · Hardware   → mismo detalle (inventario ampliado)
 *       · Discos     → unidades con barra de uso
 *       · Software   → /api/equipos/{id}/software (carga perezosa + buscador)
 *       · Mantenimiento → /api/equipos/{id}/mantenimientos, con botón para
 *         programar (POST /api/maintenance/programar), cancelar o cerrar
 *         mantenimientos (POST /api/maintenance/{id}/cerrar).
 */

// ============ Estado en memoria y referencias al DOM ============

// selectedEquipo: equipo abierto en el diálogo (id, guid, nombre…).
// softwareLoadedFor / mantenimientosLoadedFor: id del equipo cuyos datos
// ya están cargados (evitan repetir peticiones al cambiar de pestaña).
const state = {
  equipos: [],
  refreshing: false,
  selectedEquipo: null,
  software: [],
  softwareLoadedFor: null,
  mantenimientosLoadedFor: null,
  advancedTasks: [],
  advancedTasksLoadedFor: null,
  closingMaintenanceId: null,  // id del mantenimiento del form de cierre abierto
  editingDiarioId: null        // id del Diario en modo edición (null = creando nuevo)
};

// Acceso directo a los elementos del HTML (página + diálogo de ficha).
const elements = {
  refreshButton: document.querySelector("#refreshButton"),
  exportCsvButton: document.querySelector("#exportCsvButton"),
  exportPdfButton: document.querySelector("#exportPdfButton"),
  lastUpdate: document.querySelector("#lastUpdate"),
  alertBox: document.querySelector("#alertBox"),
  searchInput: document.querySelector("#searchInput"),
  stateFilter: document.querySelector("#stateFilter"),
  tableBody: document.querySelector("#equipmentTableBody"),
  emptyState: document.querySelector("#emptyState"),
  totalCount: document.querySelector("#totalCount"),
  inventoryCount: document.querySelector("#inventoryCount"),
  noInventoryCount: document.querySelector("#noInventoryCount"),
  totalRam: document.querySelector("#totalRam"),
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
  detailTabs: [...document.querySelectorAll(".detail-tab")],
  detailPanels: [...document.querySelectorAll(".detail-tab-panel")],
  softwareSearch: document.querySelector("#softwareSearch"),
  softwareCount: document.querySelector("#softwareCount"),
  softwareLoading: document.querySelector("#softwareLoading"),
  softwareList: document.querySelector("#softwareList"),
  scheduleButton: document.querySelector("#scheduleButton"),
  scheduleForm: document.querySelector("#scheduleForm"),
  scheduleStart: document.querySelector("#scheduleStart"),
  scheduleMessage: document.querySelector("#scheduleMessage"),
  scheduleError: document.querySelector("#scheduleError"),
  scheduleCancel: document.querySelector("#scheduleCancel"),
  scheduleSubmit: document.querySelector("#scheduleSubmit"),
  scheduleTypeRadios: [...document.querySelectorAll('input[name="scheduleType"]')],
  diarioOptions: document.querySelector("#diarioOptions"),
  scheduleDayBoxes: [...document.querySelectorAll("#scheduleDays input[type='checkbox']")],
  scheduleEndEnabled: document.querySelector("#scheduleEndEnabled"),
  scheduleEnd: document.querySelector("#scheduleEnd"),
  closeForm: document.querySelector("#closeForm"),
  closeFormSubtitle: document.querySelector("#closeFormSubtitle"),
  closeDetail: document.querySelector("#closeDetail"),
  closeError: document.querySelector("#closeError"),
  closeCancel: document.querySelector("#closeCancel"),
  closeSubmit: document.querySelector("#closeSubmit"),
  flagOutlook: document.querySelector("#flagOutlook"),
  flagMaintenance: document.querySelector("#flagMaintenance"),
  flagWindows: document.querySelector("#flagWindows"),
  flagShutdown: document.querySelector("#flagShutdown"),
  actionOrderList: document.querySelector("#actionOrderList"),
  actionOrderItems: document.querySelector("#actionOrderItems"),
  maintenanceLoading: document.querySelector("#maintenanceLoading"),
  maintenanceList: document.querySelector("#maintenanceList"),
  maintenanceCount: document.querySelector("#maintenanceCount"),
  advancedTaskButton: document.querySelector("#advancedTaskButton"),
  advancedTaskForm: document.querySelector("#advancedTaskForm"),
  advancedDate: document.querySelector("#advancedDate"),
  advancedName: document.querySelector("#advancedName"),
  advancedCommand: document.querySelector("#advancedCommand"),
  advancedTaskError: document.querySelector("#advancedTaskError"),
  advancedTaskCancel: document.querySelector("#advancedTaskCancel"),
  advancedTaskSubmit: document.querySelector("#advancedTaskSubmit"),
  advancedTaskLoading: document.querySelector("#advancedTaskLoading"),
  advancedTaskList: document.querySelector("#advancedTaskList"),
  advancedTasksCount: document.querySelector("#advancedTasksCount")
};

// ============ Comunicación con la API ============

/*
 * GET JSON sin caché; lanza Error con el campo "error" del servidor si
 * la respuesta no es 2xx (las APIs devuelven { error: "…" } en 4xx/5xx).
 */
async function requestJson(url, options) {
  const response = await fetch(url, {
    cache: "no-store",
    headers: { Accept: "application/json" },
    ...options
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

// ============ Refresco del inventario ============

// Descarga /api/inventario, pinta resumen + tabla y marca la hora actual.
async function refreshInventory(options = {}) {
  if (state.refreshing) return;

  const silencioso = options.silent === true;
  state.refreshing = true;
  if (!silencioso) {
    elements.refreshButton.disabled = true;
    elements.refreshButton.textContent = "Actualizando…";
    hideAlert();
  }

  try {
    const data = await requestJson("/api/inventario");
    state.equipos = Array.isArray(data) ? data : [];
    renderSummary();
    renderTable();
    elements.lastUpdate.textContent = new Intl.DateTimeFormat("es-ES", {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit"
    }).format(new Date());
  } catch (error) {
    if (!silencioso) showAlert(`No se ha podido cargar el inventario: ${error.message}`);
  } finally {
    state.refreshing = false;
    if (!silencioso) {
      elements.refreshButton.disabled = false;
      elements.refreshButton.textContent = "Actualizar";
    }
  }
}

// Tarjetas de resumen: total equipos, con inventario, sin inventario y RAM total.
function renderSummary() {
  const conInventario = state.equipos.filter((equipo) => equipo.tieneInventario).length;
  const ramTotal = state.equipos.reduce(
    (sum, equipo) => sum + (Number(equipo.memoriaRamGB) || 0),
    0
  );

  elements.totalCount.textContent = state.equipos.length;
  elements.inventoryCount.textContent = conInventario;
  elements.noInventoryCount.textContent = state.equipos.length - conInventario;
  elements.totalRam.textContent = ramTotal > 0 ? `${formatNumber(ramTotal, 1)} GB` : "—";
}

// ============ Tabla de equipos ============

/*
 * Filtra por texto (nombre, usuario, CPU, SO…) y por estado del inventario
 * (todo/con/sin). Los equipos sin inventario se pintan con una fila atenuada.
 * Cada fila incluye el botón "Ver ficha" que abre el diálogo.
 */
function renderTable() {
  const query = elements.searchInput.value.trim().toLocaleLowerCase("es-ES");
  const filter = elements.stateFilter.value;

  const filtered = state.equipos.filter((equipo) => {
    const stateMatches =
      filter === "all" ||
      (filter === "with" && equipo.tieneInventario) ||
      (filter === "without" && !equipo.tieneInventario);

    const searchable = [
      equipo.nombreEquipo,
      equipo.responsable,
      equipo.dominio,
      equipo.usuario,
      equipo.direccionIP,
      equipo.fabricante,
      equipo.modelo,
      equipo.numeroSerie,
      equipo.procesador,
      equipo.versionWindows,
      equipo.arquitectura
    ].filter(Boolean).join(" ").toLocaleLowerCase("es-ES");

    return stateMatches && (!query || searchable.includes(query));
  });

  elements.tableBody.innerHTML = "";
  elements.emptyState.classList.toggle("hidden", filtered.length !== 0);
  document.querySelector(".table-wrap").classList.toggle("hidden", filtered.length === 0);

  for (const equipo of filtered) {
    const row = document.createElement("tr");
    const online = isOnline(equipo);
    const pill = statusPill(online);

    if (!equipo.tieneInventario) {
      row.innerHTML = `
        <td><span class="status-pill ${pill}">${online ? "Online" : "Offline"}</span></td>
        <td>
          <span class="computer-name">${escapeHtml(equipo.nombreEquipo || "Sin nombre")}</span>
          <span class="computer-domain">${escapeHtml(equipo.dominio || "Sin dominio")}</span>
        </td>
        <td>${escapeHtml(equipo.responsable || "—")}</td>
        <td colspan="10" class="cell-muted">Pendiente de primer inventario</td>
        <td><button class="details-button open-detail" type="button">Ver ficha</button></td>
        <td><button class="details-button export-row" type="button" title="Exportar este equipo">Exportar</button></td>
      `;
    } else {
      row.innerHTML = `
        <td><span class="status-pill ${pill}">${online ? "Online" : "Offline"}</span></td>
        <td>
          <span class="computer-name">${escapeHtml(equipo.nombreEquipo || "Sin nombre")}</span>
          <span class="computer-domain">${escapeHtml(equipo.dominio || "Sin dominio")}</span>
        </td>
        <td>${escapeHtml(equipo.responsable || "—")}</td>
        <td>${escapeHtml(fabricanteModelo(equipo))}</td>
        <td>${escapeHtml(equipo.numeroSerie || "—")}</td>
        <td title="${escapeHtml(equipo.procesador || "")}">${escapeHtml(shortText(equipo.procesador, 42))}</td>
        <td>${escapeHtml(equipo.nucleos || "—")}</td>
        <td>${escapeHtml(formatRam(equipo.memoriaRamGB))}</td>
        <td title="${escapeHtml(equipo.versionWindows || "")}">${escapeHtml(shortText(equipo.versionWindows, 34))}</td>
        <td>${escapeHtml(equipo.ipPrincipal || equipo.direccionIP || "—")}</td>
        <td title="${escapeHtml(formatFullDate(equipo.fechaInventario))}">${escapeHtml(formatRelativeDate(equipo.fechaInventario))}</td>
        <td><button class="details-button open-detail" type="button">Ver ficha</button></td>
        <td><button class="details-button export-row" type="button" title="Exportar este equipo">Exportar</button></td>
      `;
    }

    row.querySelector(".open-detail").addEventListener("click", () => openDetails(equipo));
    row.querySelector(".export-row").addEventListener("click", (event) => {
      event.stopPropagation();
      showExportMenu(equipo, event.currentTarget);
    });
    elements.tableBody.appendChild(row);
  }
}

// ---------- Ficha del equipo (diálogo modal) ----------

/*
 * Abre la ficha: muestra el indicador de carga, pide el detalle completo
 * (/api/equipos/{id}/detalle) y deja las pestañas de software y
 * mantenimiento pendientes de carga (se piden al activarlas).
 */
async function openDetails(equipo) {
  state.selectedEquipo = equipo;
  state.software = [];
  state.softwareLoadedFor = null;
  state.mantenimientosLoadedFor = null;
  state.advancedTasks = [];
  state.advancedTasksLoadedFor = null;

  elements.softwareSearch.value = "";
  elements.softwareCount.textContent = "Sin cargar";
  elements.softwareList.innerHTML = "";
  elements.maintenanceList.innerHTML = "";
  elements.advancedTaskList.innerHTML = "";
  elements.advancedTasksCount.textContent = "Sin cargar";
  hideScheduleForm();
  hideCloseForm();
  hideAdvancedTaskForm();

  elements.detailName.textContent = equipo.nombreEquipo || "Equipo sin nombre";
  const online = isOnline(equipo);
  elements.detailStatus.innerHTML =
    `<span class="status-pill ${statusPill(online)}">${online ? "Online" : "Offline"}</span>`;
  elements.detailLoading.classList.remove("hidden");
  elements.detailContent.classList.add("hidden");
  elements.detailsDialog.showModal();
  activateDetailTab("general");

  try {
    const detalle = await requestJson(`/api/equipos/${equipo.id}/detalle`);
    renderEquipmentDetails(detalle);
    elements.detailLoading.classList.add("hidden");
    elements.detailContent.classList.remove("hidden");
  } catch (error) {
    elements.detailLoading.textContent = `No se ha podido cargar el detalle: ${error.message}`;
  }
}

// Rellena las pestañas General, Hardware y Discos con la respuesta del detalle.
function renderEquipmentDetails(detalle) {
  const equipo = detalle?.equipo || {};
  const inventario = detalle?.inventario;
  const discos = Array.isArray(detalle?.discos) ? detalle.discos : [];

  elements.detailList.innerHTML = renderDetailItems([
    ["Responsable", state.selectedEquipo?.responsable],
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
    elements.inventoryList.innerHTML =
      `<div class="detail-empty">Este equipo todavía no ha enviado inventario.</div>`;
  }

  elements.diskCount.textContent =
    discos.length === 1 ? "1 unidad" : `${discos.length} unidades`;
  elements.diskList.innerHTML = discos.length
    ? discos.map(renderDisk).join("")
    : `<div class="detail-empty">No hay información de discos disponible.</div>`;
}

// Cambia de pestaña y dispara la carga diferida (software/mantenimientos).
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
  if (tabName === "maintenance") loadMaintenanceForSelectedEquipment();
  if (tabName === "advanced") loadAdvancedTasksForSelectedEquipment();
}

// ---------- Software de un equipo (carga perezosa) ----------

/*
 * GET /api/equipos/{id}/software la primera vez que se abre la pestaña;
 * state.softwareLoadedFor guarda a qué equipo pertenecen los datos.
 */
async function loadSoftwareForSelectedEquipment() {
  const equipo = state.selectedEquipo;
  if (!equipo || state.softwareLoadedFor === equipo.id) return;

  elements.softwareLoading.classList.remove("hidden");
  elements.softwareList.innerHTML = "";

  try {
    const software = await requestJson(`/api/equipos/${equipo.id}/software`);
    state.software = Array.isArray(software) ? software : [];
    state.softwareLoadedFor = equipo.id;
    renderSoftware();
  } catch (error) {
    elements.softwareList.innerHTML =
      `<div class="detail-empty">No se ha podido cargar el software: ${escapeHtml(error.message)}</div>`;
    elements.softwareCount.textContent = "Error";
  } finally {
    elements.softwareLoading.classList.add("hidden");
  }
}

// Filtra la lista por el buscador local (nombre, versión, fabricante, arquitectura).
function renderSoftware() {
  const query = elements.softwareSearch.value.trim().toLocaleLowerCase("es-ES");
  const filtered = state.software.filter((item) => {
    const searchable = [item.nombre, item.version, item.fabricante, item.arquitectura]
      .filter(Boolean).join(" ").toLocaleLowerCase("es-ES");
    return !query || searchable.includes(query);
  });

  elements.softwareCount.textContent =
    `${filtered.length} de ${state.software.length} programas`;
  elements.softwareList.innerHTML = filtered.length
    ? filtered.map(renderSoftwareItem).join("")
    : `<div class="detail-empty">${state.software.length
        ? "No hay coincidencias con la búsqueda."
        : "Este equipo todavía no ha enviado inventario de software."}</div>`;
}

// HTML de una tarjeta de programa dentro de la pestaña.
function renderSoftwareItem(item) {
  return `
    <article class="software-item">
      <div class="software-main">
        <strong>${escapeHtml(item.nombre || "Sin nombre")}</strong>
        <span>${escapeHtml(item.fabricante || "Fabricante no indicado")}</span>
      </div>
      <div class="software-meta">
        <span><b>Versión</b>${escapeHtml(item.version || "—")}</span>
        <span><b>Arquitectura</b>${escapeHtml(item.arquitectura || "—")}</span>
        <span><b>Instalado</b>${escapeHtml(formatDateOnly(item.fechaInstalacion))}</span>
      </div>
    </article>`;
}

// ---------- Mantenimientos y programación ----------

/*
 * GET /api/equipos/{id}/mantenimientos al abrir la pestaña (una sola vez
 * por equipo; para recargar se limpia mantenimientosLoadedFor).
 */
async function loadMaintenanceForSelectedEquipment() {
  const equipo = state.selectedEquipo;
  if (!equipo || state.mantenimientosLoadedFor === equipo.id) return;

  elements.maintenanceLoading.classList.remove("hidden");
  elements.maintenanceList.innerHTML = "";

  try {
    const mantenimientos = await requestJson(`/api/equipos/${equipo.id}/mantenimientos`);
    state.mantenimientosLoadedFor = equipo.id;
    renderMaintenanceList(Array.isArray(mantenimientos) ? mantenimientos : []);
  } catch (error) {
    elements.maintenanceList.innerHTML =
      `<div class="detail-empty">No se han podido cargar los mantenimientos: ${escapeHtml(error.message)}</div>`;
  } finally {
    elements.maintenanceLoading.classList.add("hidden");
  }
}

/*
 * Pinta la lista de mantenimientos: tarjeta con acento de color según el
 * estado, fechas, chips de acciones activas (✓), mensaje y botones:
 *  · Cancelar → "Programado" y "Diario".
 *  · Cerrar   → todos menos "Completado", "Cerrado" (legado) y
 *    "Cancelado" (cierre manual: el servidor pone "Completado",
 *    FechaFin = hora del clic). Actualiza el contador.
 */
function renderMaintenanceList(mantenimientos) {
  const programados = mantenimientos.filter(
    (item) => String(item.estado || "").toLocaleLowerCase("es-ES") === "programado"
  ).length;
  const diarios = mantenimientos.filter(
    (item) => String(item.estado || "").toLocaleLowerCase("es-ES") === "diario"
  ).length;

  const partes = [`${mantenimientos.length} ${mantenimientos.length === 1 ? "registro" : "registros"}`];
  if (programados > 0) partes.push(`${programados} programado${programados === 1 ? "" : "s"}`);
  if (diarios > 0) partes.push(`${diarios} diario${diarios === 1 ? "" : "s"}`);
  elements.maintenanceCount.textContent = mantenimientos.length === 0 ? "Sin registros" : partes.join(" · ");

  if (mantenimientos.length === 0) {
    elements.maintenanceList.innerHTML =
      `<div class="detail-empty">Este equipo todavía no tiene mantenimientos registrados.</div>`;
    return;
  }

  elements.maintenanceList.innerHTML = mantenimientos.map((item) => {
    const estado = String(item.estado || "").trim();
    const estadoNorm = estado.toLocaleLowerCase("es-ES");
    const esProgramado = estadoNorm === "programado";
    const esDiario = estadoNorm === "diario";
    const esCerrable = !["completado", "cerrado", "cancelado"].includes(estadoNorm);
    const cardClass = estadoNorm === "programado" ? "is-planned"
      : estadoNorm === "diario" ? "is-diario"
      : estadoNorm === "ejecutando" || estadoNorm === "enproceso" ? "is-running"
      : estadoNorm === "cerrado" ? "is-closed"
      : estadoNorm === "cancelado" ? "is-cancelled" : "is-done";

    const acciones = [
      [item.copiaOutlook, "Copia Outlook", false],
      [item.mantenimiento, "Mantenimiento", false],
      [item.reparacionWindows, "Reparación", false],
      [item.apagarEquipo, "Apagar equipo", true]
    ].filter(([active]) => active);

    const accionesHtml = acciones.length
      ? acciones.map(([, label, danger]) =>
          `<span class="action-chip${danger ? " danger" : ""}">${escapeHtml(label)}</span>`
        ).join("")
      : `<span class="maintenance-no-actions">Sin acciones de mantenimiento</span>`;

    const duracion = Number(item.duracionSegundos || 0);
    const duracionTexto = duracion > 0
      ? ` <span>· ${escapeHtml(formatDuration(duracion))}</span>` : "";

    // Abiertos (Programado/Ejecutando/Diario): fin provisional = inicio,
    // solo se muestra "desde". Cerrados: "desde … hasta …".
    const esAbierto = ["programado", "ejecutando", "diario", "enproceso"].includes(estadoNorm);
    let fechasHtml;
    if (esDiario) {
      fechasHtml = renderDiarioSchedule(item);
    } else if (esAbierto) {
      fechasHtml = `<span>Desde</span> ${escapeHtml(formatDateTime(item.fechaInicio))}`;
    } else {
      fechasHtml = `${escapeHtml(formatDateTime(item.fechaInicio))} <span>hasta</span> ${escapeHtml(formatDateTime(item.fechaFin))}${duracionTexto}`;
    }

    return `
      <article class="maintenance-card ${cardClass}">
        <div class="maintenance-header">
          <div>
            <span class="status-pill ${maintenancePill(estado)}">${escapeHtml(estado || "Sin estado")}</span>
            <span class="maintenance-when">${fechasHtml}</span>
          </div>
          <div class="maintenance-buttons">
            ${esDiario
              ? `<button class="details-button edit-maintenance" type="button" data-id="${item.id}">Editar</button>`
              : ""}
            ${esProgramado || esDiario
              ? `<button class="details-button cancel-maintenance" type="button" data-id="${item.id}">Cancelar</button>`
              : ""}
            ${esCerrable
              ? `<button class="details-button close-maintenance" type="button" data-id="${item.id}">Cerrar</button>`
              : ""}
          </div>
        </div>
        <div class="maintenance-actions">${accionesHtml}</div>
        ${item.mensaje ? `<p class="maintenance-message">${escapeHtml(item.mensaje)}</p>` : ""}
        ${item.detalleTecnico ? `<p class="maintenance-detail">${escapeHtml(item.detalleTecnico)}</p>` : ""}
      </article>`;
  }).join("");

  for (const button of elements.maintenanceList.querySelectorAll(".cancel-maintenance")) {
    button.addEventListener("click", () => cancelMaintenance(Number(button.dataset.id)));
  }

  for (const button of elements.maintenanceList.querySelectorAll(".edit-maintenance")) {
    button.addEventListener("click", () => startEditDiario(Number(button.dataset.id), mantenimientos));
  }

  for (const button of elements.maintenanceList.querySelectorAll(".close-maintenance")) {
    button.addEventListener("click", () => closeMaintenance(Number(button.dataset.id)));
  }
}

// Texto de programación de un Diario: "Cada lun, mié · a las 15:00 · hasta …".
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
  if (item.vigenciaFin) {
    html += ` <span>hasta</span> ${escapeHtml(formatDateOnly(item.vigenciaFin))}`;
  } else {
    html += ` <span>· sin fin</span>`;
  }
  return html;
}

// POST /api/maintenance/programar/{id}/cancelar y recarga la lista.
async function cancelMaintenance(id) {
  if (!window.confirm(`¿Cancelar el mantenimiento ${id}?`)) return;

  try {
    await requestJson(`/api/maintenance/programar/${id}/cancelar`, { method: "POST" });
    state.mantenimientosLoadedFor = null;
    await loadMaintenanceForSelectedEquipment();
  } catch (error) {
    alert(`No se ha podido cancelar el mantenimiento: ${error.message}`);
  }
}

/*
 * Cierre manual: abre el formulario flotante con el detalle opcional.
 * "Cerrar ahora" → POST /api/maintenance/{id}/cerrar (el servidor pone
 * "Completado", FechaFin = hora del clic y calcula la duración).
 */
function closeMaintenance(id) {
  state.closingMaintenanceId = id;
  elements.closeFormSubtitle.textContent =
    `Se cerrará el mantenimiento ${id} con la fecha y hora actuales.`;
  elements.closeDetail.value = "";
  hideCloseError();
  hideScheduleForm();
  elements.closeForm.classList.remove("hidden");
  elements.closeDetail.focus();
}

// Oculta el formulario de cierre sin enviar.
function hideCloseForm() {
  elements.closeForm.classList.add("hidden");
  state.closingMaintenanceId = null;
  hideCloseError();
}

// Envía el cierre al servidor y recarga la lista.
async function submitCloseForm(event) {
  event.preventDefault();
  hideCloseError();

  const id = state.closingMaintenanceId;
  if (!id) return;

  elements.closeSubmit.disabled = true;

  try {
    await requestJson(`/api/maintenance/${id}/cerrar`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ detalleTecnico: elements.closeDetail.value.trim() || null })
    });

    hideCloseForm();
    state.mantenimientosLoadedFor = null;
    await loadMaintenanceForSelectedEquipment();
  } catch (error) {
    showCloseError(error.message);
  } finally {
    elements.closeSubmit.disabled = false;
  }
}

// Muestra/oculta el error de validación del formulario de cierre.
function showCloseError(message) {
  elements.closeError.textContent = message;
  elements.closeError.classList.remove("hidden");
}

function hideCloseError() {
  elements.closeError.classList.add("hidden");
  elements.closeError.textContent = "";
}

// Muestra el formulario de programación con valor por defecto
// (inicio = dentro de 1 h redondeado a la hora). No hay campo de fin para el
// tipo Único: lo escribe quien cierre el mantenimiento (agente o botón
// "Cerrar"). El tipo Diario añade días de semana y una fecha de fin opcional.
// Abre el formulario en modo edición de un Diario existente: pre-carga
// tipo, días, hora, vigencia, acciones, orden y mensaje. Al enviar se
// llama a PUT /api/maintenance/diario/editar en vez de crear uno nuevo.
function startEditDiario(id, mantenimientos) {
  const item = mantenimientos.find((m) => m.id === id);
  if (!item) return;

  state.editingDiarioId = id;

  // Tipo fijado en Diario.
  const diarioRadio = elements.scheduleTypeRadios.find((r) => r.value === "Diario");
  if (diarioRadio) diarioRadio.checked = true;
  applyScheduleType();

  // Hora de inicio (solo la hora importa en un Diario).
  elements.scheduleStart.min = "";
  const fecha = parseDate(item.fechaInicio);
  if (fecha) {
    const pad = (v) => String(v).padStart(2, "0");
    elements.scheduleStart.value =
      `${fecha.getFullYear()}-${pad(fecha.getMonth() + 1)}-${pad(fecha.getDate())}T${pad(fecha.getHours())}:${pad(fecha.getMinutes())}`;
  }

  // Días de la semana.
  const dias = Array.isArray(item.dias) ? item.dias.map(Number) : [];
  elements.scheduleDayBoxes.forEach((box) => {
    box.checked = dias.includes(Number(box.value));
  });

  // Vigencia (fecha de fin).
  if (item.vigenciaFin) {
    elements.scheduleEndEnabled.checked = true;
    elements.scheduleEnd.disabled = false;
    const fin = parseDate(item.vigenciaFin);
    if (fin) {
      const pad = (v) => String(v).padStart(2, "0");
      elements.scheduleEnd.value = `${fin.getFullYear()}-${pad(fin.getMonth() + 1)}-${pad(fin.getDate())}`;
    }
  } else {
    elements.scheduleEndEnabled.checked = false;
    elements.scheduleEnd.disabled = true;
    elements.scheduleEnd.value = "";
  }

  // Acciones.
  elements.flagOutlook.checked = !!item.copiaOutlook;
  elements.flagMaintenance.checked = !!item.mantenimiento;
  elements.flagWindows.checked = !!item.reparacionWindows;
  elements.flagShutdown.checked = !!item.apagarEquipo;

  // Reconstruir el orden de prioridad desde el JSON del servidor.
  actionOrder = [];
  const ordenServidor = Array.isArray(item.ordenAcciones) ? item.ordenAcciones : [];
  const mapToId = { "CopiaOutlook": "outlook", "Mantenimiento": "maintenance", "ReparacionWindows": "windows", "ApagarEquipo": "shutdown" };
  for (const nombre of ordenServidor) {
    const idAccion = mapToId[nombre];
    if (idAccion) actionOrder.push(idAccion);
  }
  // Añadir las marcadas que falten.
  if (elements.flagOutlook.checked && !actionOrder.includes("outlook")) actionOrder.push("outlook");
  if (elements.flagMaintenance.checked && !actionOrder.includes("maintenance")) actionOrder.push("maintenance");
  if (elements.flagWindows.checked && !actionOrder.includes("windows")) actionOrder.push("windows");
  if (elements.flagShutdown.checked) {
    actionOrder = actionOrder.filter((a) => a !== "shutdown");
    actionOrder.push("shutdown");
  }
  renderActionOrder();

  // Mensaje.
  elements.scheduleMessage.value = item.mensaje || "";

  // Cambiar texto del botón para indicar modo edición.
  elements.scheduleSubmit.textContent = "Guardar cambios";
  elements.scheduleForm.querySelector(".schedule-form-header strong").textContent = "Editar mantenimiento Diario";

  hideCloseForm();
  elements.scheduleForm.classList.remove("hidden");
  hideScheduleError();
  elements.scheduleStart.focus();
}

function showScheduleForm() {
  elements.scheduleForm.classList.remove("hidden");
  hideScheduleError();
  hideCloseForm();

  // No se puede elegir una hora pasada: mínimo 1 minuto más que ahora.
  elements.scheduleStart.min = toLocalInputValue(new Date(Date.now() + 60000));

  if (!elements.scheduleStart.value) {
    const inicio = new Date(Date.now() + 60 * 60 * 1000);
    inicio.setMinutes(0, 0, 0);
    elements.scheduleStart.value = toLocalInputValue(inicio);
  }

  applyScheduleType();
  elements.scheduleStart.focus();
}

// Oculta el formulario sin enviar y sale del modo edición.
function hideScheduleForm() {
  elements.scheduleForm.classList.add("hidden");
  hideScheduleError();
  state.editingDiarioId = null;
  elements.scheduleSubmit.textContent = "Guardar programación";
  elements.scheduleForm.querySelector(".schedule-form-header strong").textContent = "Nuevo mantenimiento";
}

// Tipo seleccionado en el radio ("Unico" | "Diario").
function selectedScheduleType() {
  const checked = elements.scheduleTypeRadios.find((radio) => radio.checked);
  return checked ? checked.value : "Unico";
}

// Muestra u oculta las opciones exclusivas del Diario (días + fecha de fin)
// según el tipo elegido en el formulario.
function applyScheduleType() {
  const esDiario = selectedScheduleType() === "Diario";
  elements.diarioOptions.classList.toggle("hidden", !esDiario);
}

// Habilita o deshabilita el campo de fecha de fin según el checkbox.
function applyEndEnabled() {
  elements.scheduleEnd.disabled = !elements.scheduleEndEnabled.checked;
  if (!elements.scheduleEndEnabled.checked) {
    elements.scheduleEnd.value = "";
  }
}

// ============ Lista de prioridad de acciones ============

// Mapeo id → etiqueta para las acciones del mantenimiento.
const ACCIONES_LABELS = {
  outlook: "Copia Outlook",
  maintenance: "Mantenimiento",
  windows: "Reparación",
  shutdown: "Apagar equipo"
};

// Orden actual de las acciones marcadas (array de strings: "outlook", "windows"…).
// Se mantiene en memoria para no perder el orden al re-marcar checkboxes.
let actionOrder = [];

// Repinta la lista de prioridad según las acciones marcadas.
// Conserva el orden existente; las nuevas se añaden al final.
// "shutdown" siempre se fuerza al final.
function renderActionOrder() {
  const checked = [];
  if (elements.flagOutlook.checked) checked.push("outlook");
  if (elements.flagMaintenance.checked) checked.push("maintenance");
  if (elements.flagWindows.checked) checked.push("windows");
  if (elements.flagShutdown.checked) checked.push("shutdown");

  // Conservar el orden previo de las que siguen marcadas.
  actionOrder = actionOrder.filter((id) => checked.includes(id));
  // Añadir las nuevas al final.
  for (const id of checked) {
    if (!actionOrder.includes(id)) actionOrder.push(id);
  }
  // shutdown siempre al final.
  actionOrder = actionOrder.filter((id) => id !== "shutdown");
  if (elements.flagShutdown.checked) actionOrder.push("shutdown");

  const tieneAcciones = actionOrder.length > 0;
  elements.actionOrderList.classList.toggle("hidden", !tieneAcciones);

  elements.actionOrderItems.innerHTML = actionOrder.map((id, index) => {
    const esShutdown = id === "shutdown";
    return `
      <div class="action-order-item${esShutdown ? " is-shutdown" : ""}" data-id="${id}">
        <span class="action-order-num">${index + 1}</span>
        <span class="action-order-name">${ACCIONES_LABELS[id] || id}</span>
        <button type="button" class="order-btn move-up" data-id="${id}" ${index === 0 || esShutdown ? "disabled" : ""} title="Subir">↑</button>
        <button type="button" class="order-btn move-down" data-id="${id}" ${index === actionOrder.length - 1 || esShutdown ? "disabled" : ""} title="Bajar">↓</button>
      </div>`;
  }).join("");

  for (const btn of elements.actionOrderItems.querySelectorAll(".move-up")) {
    btn.addEventListener("click", () => moveAction(btn.dataset.id, -1));
  }
  for (const btn of elements.actionOrderItems.querySelectorAll(".move-down")) {
    btn.addEventListener("click", () => moveAction(btn.dataset.id, 1));
  }
}

// Mueve una acción una posición arriba (-1) o abajo (+1) en la lista.
function moveAction(id, delta) {
  const index = actionOrder.indexOf(id);
  const target = index + delta;
  // No mover shutdown ni salir de los límites.
  if (id === "shutdown" || target < 0 || target >= actionOrder.length) return;
  // No mover nada por encima o por debajo de shutdown.
  if (actionOrder[target] === "shutdown") return;
  [actionOrder[index], actionOrder[target]] = [actionOrder[target], actionOrder[index]];
  renderActionOrder();
}

// Devuelve el orden actual como array de nombres legibles para el servidor.
function currentActionOrder() {
  const map = {
    outlook: "CopiaOutlook",
    maintenance: "Mantenimiento",
    windows: "ReparacionWindows",
    shutdown: "ApagarEquipo"
  };
  return [...new Set(actionOrder.map((id) => map[id] || id))];
}

/*
 * Envía la programación: monta el JSON con el GUID del equipo, la fecha de
 * inicio en ISO y las 4 acciones marcadas. Para el tipo Diario añade los
 * días de la semana y, si se habilitó, la fecha de fin; si el servidor
 * responde bien, limpia el formulario y recarga la lista de mantenimientos.
 */
async function submitSchedule(event) {
  event.preventDefault();
  hideScheduleError();

  const equipo = state.selectedEquipo;
  if (!equipo) return;

  const fechaInicio = elements.scheduleStart.value;
  const tipo = selectedScheduleType();

  if (!fechaInicio) {
    showScheduleError("Indica la fecha de inicio.");
    return;
  }

  // Al CREAR, la fecha/hora de inicio no puede ser pasada: como mínimo 1
  // minuto más que la hora actual. (Al editar un Diario se respeta la fecha
  // original, que puede ser anterior.)
  if (state.editingDiarioId === null) {
    const inicio = new Date(fechaInicio);
    if (isNaN(inicio.getTime()) || inicio.getTime() < Date.now() + 60000) {
      showScheduleError("La fecha de inicio debe ser al menos 1 minuto posterior a la hora actual.");
      return;
    }
  }

  // Validar días (común a creación y edición de Diario).
  const dias = elements.scheduleDayBoxes
    .filter((box) => box.checked)
    .map((box) => Number(box.value));

  if (tipo === "Diario" && dias.length === 0) {
    showScheduleError("Selecciona al menos un día de la semana.");
    return;
  }

  elements.scheduleSubmit.disabled = true;

  // ---- MODO EDICIÓN de un Diario existente ----
  if (state.editingDiarioId !== null) {
    const bodyEdicion = {
      mantenimientoId: state.editingDiarioId,
      fechaInicio: `${fechaInicio}:00`,
      dias,
      copiaOutlook: elements.flagOutlook.checked,
      mantenimiento: elements.flagMaintenance.checked,
      reparacionWindows: elements.flagWindows.checked,
      apagarEquipo: elements.flagShutdown.checked,
      ordenAcciones: currentActionOrder(),
      mensaje: elements.scheduleMessage.value.trim() || null
    };

    if (elements.scheduleEndEnabled.checked && elements.scheduleEnd.value) {
      bodyEdicion.fechaFin = `${elements.scheduleEnd.value}T23:59:59`;
    }

    try {
      await requestJson("/api/maintenance/diario/editar", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(bodyEdicion)
      });

      hideScheduleForm();
      state.mantenimientosLoadedFor = null;
      await loadMaintenanceForSelectedEquipment();
    } catch (error) {
      showScheduleError(error.message);
    } finally {
      elements.scheduleSubmit.disabled = false;
    }
    return;
  }

  // ---- MODO CREACIÓN (nuevo mantenimiento) ----
  const body = {
    guid: equipo.guid,
    tipo,
    // La hora local tal cual ("2026-10-13T15:00:00"), SIN toISOString():
    // eso convertía a UTC y el servidor guardaba 2 h menos (15:00 → 13:00).
    fechaInicio: `${fechaInicio}:00`,
    copiaOutlook: elements.flagOutlook.checked,
    mantenimiento: elements.flagMaintenance.checked,
    reparacionWindows: elements.flagWindows.checked,
    apagarEquipo: elements.flagShutdown.checked,
    ordenAcciones: currentActionOrder(),
    mensaje: elements.scheduleMessage.value.trim() || null
  };

  if (tipo === "Diario") {
    body.dias = dias;

    // La fecha de fin es opcional: solo se envía si el usuario la habilitó
    // y eligió una fecha.
    if (elements.scheduleEndEnabled.checked && elements.scheduleEnd.value) {
      body.fechaFin = `${elements.scheduleEnd.value}T23:59:59`;
    }
  }

  try {
    await requestJson("/api/maintenance/programar", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });

    elements.scheduleMessage.value = "";
    elements.flagOutlook.checked = false;
    elements.flagMaintenance.checked = false;
    elements.flagWindows.checked = false;
    elements.flagShutdown.checked = false;
    actionOrder = [];
    renderActionOrder();
    elements.scheduleDayBoxes.forEach((box) => {
      // Lunes a viernes marcados por defecto; fin de semana no.
      box.checked = Number(box.value) >= 1 && Number(box.value) <= 5;
    });
    elements.scheduleEndEnabled.checked = false;
    elements.scheduleEnd.value = "";
    elements.scheduleEnd.disabled = true;
    hideScheduleForm();
    state.mantenimientosLoadedFor = null;
    await loadMaintenanceForSelectedEquipment();
  } catch (error) {
    showScheduleError(error.message);
  } finally {
    elements.scheduleSubmit.disabled = false;
  }
}

// Muestra/oculta el mensaje de error de validación del formulario.
function showScheduleError(message) {
  elements.scheduleError.textContent = message;
  elements.scheduleError.classList.remove("hidden");
}

function hideScheduleError() {
  elements.scheduleError.classList.add("hidden");
  elements.scheduleError.textContent = "";
}

// ---------- Tareas avanzadas (PowerShell) ----------

/*
 * GET /api/equipos/{id}/advanced-tasks la primera vez que se abre la
 * pestaña. Cada tarea es un comando de PowerShell con fecha de ejecución.
 */
async function loadAdvancedTasksForSelectedEquipment() {
  const equipo = state.selectedEquipo;
  if (!equipo || state.advancedTasksLoadedFor === equipo.id) return;

  elements.advancedTaskLoading.classList.remove("hidden");
  elements.advancedTaskList.innerHTML = "";

  try {
    const tareas = await requestJson(`/api/equipos/${equipo.id}/advanced-tasks`);
    state.advancedTasks = Array.isArray(tareas) ? tareas : [];
    state.advancedTasksLoadedFor = equipo.id;
    renderAdvancedTasks(state.advancedTasks);
  } catch (error) {
    elements.advancedTaskList.innerHTML =
      `<div class="detail-empty">No se han podido cargar las tareas avanzadas: ${escapeHtml(error.message)}</div>`;
    elements.advancedTasksCount.textContent = "Error";
  } finally {
    elements.advancedTaskLoading.classList.add("hidden");
  }
}

// Pinta las tareas avanzadas con su estado, comando y resultado.
function renderAdvancedTasks(tareas) {
  const programadas = tareas.filter(
    (item) => String(item.estado || "").toLocaleLowerCase("es-ES") === "programada"
  ).length;
  const ejecutando = tareas.filter(
    (item) => String(item.estado || "").toLocaleLowerCase("es-ES") === "ejecutando"
  ).length;

  const partes = [`${tareas.length} ${tareas.length === 1 ? "tarea" : "tareas"}`];
  if (programadas > 0) partes.push(`${programadas} programada${programadas === 1 ? "" : "s"}`);
  if (ejecutando > 0) partes.push(`${ejecutando} ejecutando`);
  elements.advancedTasksCount.textContent = tareas.length === 0 ? "Sin tareas" : partes.join(" · ");

  if (tareas.length === 0) {
    elements.advancedTaskList.innerHTML =
      `<div class="detail-empty">Este equipo todavía no tiene tareas avanzadas programadas.</div>`;
    return;
  }

  elements.advancedTaskList.innerHTML = tareas.map((item) => {
    const estado = String(item.estado || "").trim();
    const estadoNorm = estado.toLocaleLowerCase("es-ES");
    const esProgramada = estadoNorm === "programada";
    const cardClass = estadoNorm === "programada" ? "is-planned"
      : estadoNorm === "ejecutando" ? "is-running"
      : estadoNorm === "cancelada" ? "is-cancelled"
      : estadoNorm === "error" ? "is-error" : "is-done";

    const descripcion = item.descripcion
      ? `<p class="maintenance-message">${escapeHtml(item.descripcion)}</p>`
      : "";

    let fechas = `<span>Programada para</span> ${escapeHtml(formatDateTime(item.fechaEjecucion))}`;
    if (item.fechaInicioEjecucion) {
      fechas += ` <span>· iniciada</span> ${escapeHtml(formatDateTime(item.fechaInicioEjecucion))}`;
    }
    if (item.fechaFinEjecucion) {
      fechas += ` <span>· finalizada</span> ${escapeHtml(formatDateTime(item.fechaFinEjecucion))}`;
    }
    if (Number(item.duracionSegundos || 0) > 0) {
      fechas += ` <span>· ${escapeHtml(formatDuration(item.duracionSegundos))}</span>`;
    }
    if (item.codigoSalida !== null && item.codigoSalida !== undefined) {
      fechas += ` <span>· código</span> ${escapeHtml(item.codigoSalida)}`;
    }

    return `
      <article class="maintenance-card ${cardClass}">
        <div class="maintenance-header">
          <div>
            <span class="status-pill ${advancedTaskPill(estado)}">${escapeHtml(estado || "Sin estado")}</span>
            <span class="maintenance-when">${fechas}</span>
          </div>
          <div class="maintenance-buttons">
            ${esProgramada
              ? `<button class="details-button cancel-advanced-task" type="button" data-id="${item.id}">Cancelar</button>`
              : ""}
          </div>
        </div>
        ${descripcion}
        <pre class="advanced-task-command">${escapeHtml(item.comando || "")}</pre>
        ${item.salida ? `<pre class="advanced-task-output">${escapeHtml(item.salida)}</pre>` : ""}
        ${item.error ? `<p class="maintenance-detail">${escapeHtml(item.error)}</p>` : ""}
      </article>`;
  }).join("");

  for (const button of elements.advancedTaskList.querySelectorAll(".cancel-advanced-task")) {
    button.addEventListener("click", () => cancelAdvancedTask(Number(button.dataset.id)));
  }
}

// Clase CSS de la pill según el estado de la tarea avanzada.
function advancedTaskPill(estado) {
  const value = String(estado || "").toLocaleLowerCase("es-ES");
  if (value === "programada") return "planned";
  if (value === "ejecutando") return "running";
  if (value === "error") return "failed";
  if (value === "cancelada") return "cancelled";
  return "closed";
}

// POST /api/advanced-tasks/{id}/cancel y recarga la lista.
async function cancelAdvancedTask(id) {
  if (!window.confirm(`¿Cancelar la tarea avanzada ${id}?`)) return;

  try {
    await requestJson(`/api/advanced-tasks/${id}/cancel`, { method: "POST" });
    state.advancedTasksLoadedFor = null;
    await loadAdvancedTasksForSelectedEquipment();
  } catch (error) {
    alert(`No se ha podido cancelar la tarea avanzada: ${error.message}`);
  }
}

// Muestra el formulario de programación con una fecha por defecto
// (dentro de 1 hora, redondeada a la hora).
function showAdvancedTaskForm() {
  elements.advancedTaskForm.classList.remove("hidden");
  hideAdvancedTaskError();
  hideScheduleForm();
  hideCloseForm();

  if (!elements.advancedDate.value) {
    const inicio = new Date(Date.now() + 60 * 60 * 1000);
    inicio.setMinutes(0, 0, 0);
    elements.advancedDate.value = formatAdvancedDateInput(inicio);
  }

  elements.advancedDate.focus();
}

function hideAdvancedTaskForm() {
  elements.advancedTaskForm.classList.add("hidden");
  hideAdvancedTaskError();
}

function showAdvancedTaskError(message) {
  elements.advancedTaskError.textContent = message;
  elements.advancedTaskError.classList.remove("hidden");
}

function hideAdvancedTaskError() {
  elements.advancedTaskError.classList.add("hidden");
  elements.advancedTaskError.textContent = "";
}

// Date → "dd/mm/aaaa hh:mm" para el campo de texto.
function formatAdvancedDateInput(date) {
  const pad = (value) => String(value).padStart(2, "0");
  return `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

// Acepta "dd/mm/aaaa hh:mm" y "aaaa-mm-dd hh:mm" (hora opcional).
// Devuelve un Date o null si el texto no es una fecha válida.
function parseAdvancedDate(value) {
  const texto = String(value || "").trim();
  if (!texto) return null;

  let match = texto.match(
    /^(\d{4})-(\d{1,2})-(\d{1,2})(?:[ T](\d{1,2}):(\d{2})(?::(\d{2}))?)?$/);
  let year;
  let month;
  let day;
  let hour = 0;
  let minute = 0;
  let second = 0;

  if (match) {
    year = Number(match[1]);
    month = Number(match[2]);
    day = Number(match[3]);
    hour = match[4] !== undefined ? Number(match[4]) : 0;
    minute = match[5] !== undefined ? Number(match[5]) : 0;
    second = match[6] !== undefined ? Number(match[6]) : 0;
  } else {
    match = texto.match(
      /^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:[ T](\d{1,2}):(\d{2})(?::(\d{2}))?)?$/);
    if (!match) return null;

    day = Number(match[1]);
    month = Number(match[2]);
    year = Number(match[3]);
    hour = match[4] !== undefined ? Number(match[4]) : 0;
    minute = match[5] !== undefined ? Number(match[5]) : 0;
    second = match[6] !== undefined ? Number(match[6]) : 0;
  }

  if (month < 1 || month > 12 || day < 1 || day > 31 ||
      hour > 23 || minute > 59 || second > 59) {
    return null;
  }

  const date = new Date(year, month - 1, day, hour, minute, second, 0);
  // Rechaza fechas imposibles (31/02, 30/02…): Date las desplazaría.
  if (date.getFullYear() !== year || date.getMonth() !== month - 1 ||
      date.getDate() !== day) {
    return null;
  }

  return date;
}

// Envía la tarea al servidor y recarga el historial.
async function submitAdvancedTask(event) {
  event.preventDefault();
  hideAdvancedTaskError();

  const equipo = state.selectedEquipo;
  if (!equipo) return;

  const fecha = parseAdvancedDate(elements.advancedDate.value);
  if (!fecha) {
    showAdvancedTaskError("Indica una fecha válida: dd/mm/aaaa hh:mm o aaaa-mm-dd hh:mm.");
    return;
  }

  if (fecha.getTime() < Date.now() + 60000) {
    showAdvancedTaskError("La fecha de ejecución debe ser al menos 1 minuto posterior a la hora actual.");
    return;
  }

  const comando = elements.advancedCommand.value.trim();
  if (!comando) {
    showAdvancedTaskError("Escribe el comando de PowerShell que se ejecutará.");
    return;
  }

  const body = {
    guid: equipo.guid,
    fechaEjecucion: `${toLocalInputValue(fecha)}:00`,
    descripcion: elements.advancedName.value.trim() || null,
    comando
  };

  elements.advancedTaskSubmit.disabled = true;

  try {
    await requestJson("/api/advanced-tasks", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });

    elements.advancedDate.value = "";
    elements.advancedName.value = "";
    elements.advancedCommand.value = "";
    hideAdvancedTaskForm();
    state.advancedTasksLoadedFor = null;
    await loadAdvancedTasksForSelectedEquipment();
  } catch (error) {
    showAdvancedTaskError(error.message);
  } finally {
    elements.advancedTaskSubmit.disabled = false;
  }
}

// ---------- Helpers ----------

// Date → valor para <input type="datetime-local"> ("2026-10-14T18:00").
function toLocalInputValue(date) {
  const pad = (value) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

// Pares etiqueta/valor → filas dt/dd de las pestañas del diálogo.
function renderDetailItems(items) {
  return items.map(([label, value]) => `
    <div class="detail-item">
      <dt>${escapeHtml(label)}</dt>
      <dd>${escapeHtml(value === 0 ? "0" : (value || "—"))}</dd>
    </div>
  `).join("");
}

// Tarjeta de disco con barra de uso (usados = total − libre).
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

// Clase CSS de la pill según el estado del mantenimiento.
function maintenancePill(estado) {
  const value = String(estado || "").toLocaleLowerCase("es-ES");
  if (value === "programado") return "planned";
  if (value === "diario") return "daily";
  if (value === "ejecutando" || value === "enproceso") return "running";
  if (value === "cerrado") return "closed";
  if (value === "cancelado") return "cancelled";
  if (value === "completado" || value === "finalizado" || value === "ok") return "online";
  return "offline";
}

// Clase CSS de la pill de estado del equipo (online/offline).
function statusPill(online) {
  return online ? "online" : "offline";
}

// "Fabricante Modelo" en una sola cadena ("Dell OptiPlex 7090").
function fabricanteModelo(equipo) {
  return [equipo.fabricante, equipo.modelo].filter(Boolean).join(" ") || "—";
}

// Recorta texto largo con puntos suspensivos (para la tabla).
function shortText(value, max) {
  if (!value) return "—";
  return value.length > max ? `${value.slice(0, max - 1)}…` : value;
}

// RAM en GB con 1 decimal; "—" si el valor es 0 o inválido.
function formatRam(value) {
  const ram = Number(value);
  return Number.isFinite(ram) && ram > 0 ? `${formatNumber(ram, 1)} GB` : "—";
}

// Bytes → "GB" o "TB" en formato español.
function formatBytes(value) {
  const bytes = Number(value || 0);
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 GB";
  const gb = bytes / 1073741824;
  if (gb >= 1024) return `${formatNumber(gb / 1024, 2)} TB`;
  return `${formatNumber(gb, gb >= 100 ? 0 : 1)} GB`;
}

// Segundos → "2 h 15 min" / "45 min 3 s" / "30 s".
function formatDuration(secondsValue) {
  const total = Math.max(0, Number(secondsValue || 0));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = Math.floor(total % 60);
  if (hours > 0) return `${hours} h ${minutes} min`;
  if (minutes > 0) return `${minutes} min ${seconds} s`;
  return `${seconds} s`;
}

// True si el campo estado del equipo dice "online".
function isOnline(equipo) {
  return String(equipo?.estado || "").toLocaleLowerCase("es-ES") === "online";
}

// Número en formato español con hasta N decimales.
function formatNumber(value, decimals = 0) {
  const number = Number(value);
  if (!Number.isFinite(number)) return "—";
  return new Intl.NumberFormat("es-ES", {
    minimumFractionDigits: 0,
    maximumFractionDigits: decimals
  }).format(number);
}

// Solo fecha (día/mes/año) en formato español.
function formatDateOnly(value) {
  const date = parseDate(value);
  if (!date) return "—";
  return new Intl.DateTimeFormat("es-ES", { dateStyle: "medium" }).format(date);
}

// Fecha y hora completas (para tooltips y ficha).
function formatFullDate(value) {
  const date = parseDate(value);
  if (!date) return "—";
  return new Intl.DateTimeFormat("es-ES", {
    dateStyle: "medium",
    timeStyle: "medium"
  }).format(date);
}

// Fecha y hora compactas para las tarjetas de mantenimiento ("14 oct 2026 · 18:00").
function formatDateTime(value) {
  const date = parseDate(value);
  if (!date) return "—";
  return new Intl.DateTimeFormat("es-ES", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit"
  }).format(date).replace(",", " ·");
}

// Fecha relativa en español ("hace 5 minutos") para la tabla.
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

// ISO → Date; null si el valor no es una fecha válida.
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

// Muestra/oculta la alerta superior y el error del formulario.
function showAlert(message) {
  elements.alertBox.textContent = message;
  elements.alertBox.classList.remove("hidden");
}

function hideAlert() {
  elements.alertBox.classList.add("hidden");
  elements.alertBox.textContent = "";
}

// ============ Exportación de inventario (CSV / PDF) ============

// Devuelve los equipos filtrados actualmente visibles en la tabla.
function getFilteredEquipos() {
  const query = elements.searchInput.value.trim().toLocaleLowerCase("es-ES");
  const filter = elements.stateFilter.value;

  return state.equipos.filter((equipo) => {
    const stateMatches =
      filter === "all" ||
      (filter === "with" && equipo.tieneInventario) ||
      (filter === "without" && !equipo.tieneInventario);

    const searchable = [
      equipo.nombreEquipo, equipo.responsable, equipo.dominio, equipo.usuario,
      equipo.direccionIP, equipo.fabricante, equipo.modelo, equipo.numeroSerie,
      equipo.procesador, equipo.versionWindows, equipo.arquitectura
    ].filter(Boolean).join(" ").toLocaleLowerCase("es-ES");

    return stateMatches && (!query || searchable.includes(query));
  });
}

// Convierte una matriz de filas (arrays) a string CSV con separador ";".
function toCsv(rows) {
  return rows.map((row) =>
    row.map((cell) => {
      const value = String(cell ?? "");
      // Envolver en comillas si contiene ; " o salto de línea.
      if (value.includes(";") || value.includes('"') || value.includes("\n")) {
        return `"${value.replaceAll('"', '""')}"`;
      }
      return value;
    }).join(";")
  ).join("\r\n");
}

// Dispara la descarga de un archivo Blob como nombre dado.
function downloadBlob(blob, filename) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

// Marca de tiempo para nombres de archivo ("2026-10-14_11-30").
function fileTimestamp() {
  const d = new Date();
  const pad = (v) => String(v).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}_${pad(d.getHours())}-${pad(d.getMinutes())}`;
}

// ---------- CSV general (todos los equipos filtrados) ----------

function exportGeneralCsv() {
  const equipos = getFilteredEquipos();
  if (equipos.length === 0) { showAlert("No hay equipos para exportar."); return; }

  const esc = (v) => String(v ?? "").replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");

  const headerCells = [
    "Estado", "Equipo", "Dominio", "Responsable", "Usuario",
    "Fabricante", "Modelo", "Número de serie", "Procesador", "Núcleos",
    "RAM (GB)", "Arquitectura", "Sistema operativo", "IP", "MAC",
    "Último inventario", "GUID"
  ];

  const headerHtml = headerCells.map((c) =>
    `<td style="background:#0f6cbd;color:#fff;font-weight:bold;padding:5px 8px;white-space:nowrap">${esc(c)}</td>`
  ).join("");

  const bodyHtml = equipos.map((eq) => {
    const online = isOnline(eq);
    const estadoColor = online ? "#168252" : "#c4322b";
    return `<tr>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef;color:${estadoColor};font-weight:bold">${online ? "Online" : "Offline"}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.nombreEquipo || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.dominio || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.responsable || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.usuario || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.fabricante || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.modelo || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.numeroSerie || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(shortText(eq.procesador, 42))}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.nucleos || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(formatRam(eq.memoriaRamGB))}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.arquitectura || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(shortText(eq.versionWindows, 34))}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.ipPrincipal || eq.direccionIP || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.macPrincipal || "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(eq.fechaInventario ? formatFullDate(eq.fechaInventario) : "—")}</td>` +
      `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef;font-size:9px">${esc(eq.guid || "—")}</td>` +
      `</tr>`;
  }).join("");

  const html = `
    <html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
    <head><meta charset="utf-8"><!--[if gte mso 9]><xml><x:ExcelWorkbook><x:ExcelWorksheets><x:ExcelWorksheet>
    <x:Name>Inventario</x:Name><x:WorksheetOptions><x:DisplayGridlines/></x:WorksheetOptions>
    </x:ExcelWorksheet></x:ExcelWorksheets></x:ExcelWorkbook></xml><![endif]-->
    <style>
      body { font-family: Calibri, sans-serif; font-size: 11px; }
      td { vertical-align: top; }
    </style></head><body>
    <table border="0" cellpadding="0" cellspacing="0">
      <tr><td colspan="${headerCells.length}" style="background:#0f6cbd;color:#fff;font-weight:bold;font-size:13px;padding:8px">
        INVENTARIO DE EQUIPOS — ${equipos.length} equipos · ${esc(formatFullDate(new Date()))}
      </td></tr>
      <tr>${headerHtml}</tr>
      ${bodyHtml}
    </table></body></html>`;

  const blob = new Blob(["\uFEFF" + html], { type: "application/vnd.ms-excel;charset=utf-8" });
  downloadBlob(blob, `inventario_general_${fileTimestamp()}.xls`);
}

// ---------- PDF general (todos los equipos filtrados) ----------

async function exportGeneralPdf() {
  const equipos = getFilteredEquipos();
  if (equipos.length === 0) { showAlert("No hay equipos para exportar."); return; }
  if (typeof window.jspdf === "undefined") { showAlert("La librería PDF no está cargada. Recarga la página."); return; }

  const { jsPDF } = window.jspdf;
  const doc = new jsPDF({ orientation: "landscape", unit: "mm", format: "a4" });

  // Encabezado
  doc.setFontSize(16);
  doc.setFont(undefined, "bold");
  doc.text("Tradipres Endpoint Manager — Inventario de equipos", 14, 15);
  doc.setFontSize(10);
  doc.setFont(undefined, "normal");
  doc.setTextColor(100);
  doc.text(`Generado: ${formatFullDate(new Date())} · Total: ${equipos.length} equipos`, 14, 22);
  doc.setTextColor(0);

  const head = [[
    "Estado", "Equipo", "Responsable", "Fabricante / Modelo", "Serie",
    "Procesador", "Núcleos", "RAM", "Sistema operativo", "IP", "Últ. inventario"
  ]];

  const body = equipos.map((eq) => [
    isOnline(eq) ? "Online" : "Offline",
    `${eq.nombreEquipo || "—"}\n${eq.dominio || ""}`,
    eq.responsable || "—",
    fabricanteModelo(eq),
    eq.numeroSerie || "—",
    shortText(eq.procesador, 38),
    String(eq.nucleos || "—"),
    formatRam(eq.memoriaRamGB),
    shortText(eq.versionWindows, 30),
    eq.ipPrincipal || eq.direccionIP || "—",
    eq.fechaInventario ? formatDateOnly(eq.fechaInventario) : "—"
  ]);

  doc.autoTable({
    head,
    body,
    startY: 27,
    styles: { fontSize: 7.5, cellPadding: 2 },
    headStyles: { fillColor: [15, 108, 189], textColor: 255, fontStyle: "bold" },
    alternateRowStyles: { fillColor: [240, 245, 250] },
    columnStyles: {
      0: { cellWidth: 16 },
      1: { cellWidth: 32 },
      5: { cellWidth: 48 }
    },
    margin: { left: 14, right: 14 }
  });

  doc.save(`inventario_general_${fileTimestamp()}.pdf`);
}

// ---------- Menú de exportación por equipo (selector emergente) ----------

// Cierra el menú de exportación si está abierto.
function closeExportMenu() {
  const existing = document.querySelector(".export-menu-popup");
  if (existing) existing.remove();
}

// Muestra un pequeño selector emergente junto al botón con opciones CSV / PDF.
function showExportMenu(equipo, anchor) {
  closeExportMenu();

  const menu = document.createElement("div");
  menu.className = "export-menu-popup";
  menu.innerHTML = `
    <button type="button" data-format="csv">Excel</button>
    <button type="button" data-format="pdf">PDF</button>
  `;

  // Posicionar junto al botón que lo abrió.
  const rect = anchor.getBoundingClientRect();
  menu.style.top = `${rect.bottom + 4}px`;
  menu.style.left = `${Math.max(8, rect.left)}px`;
  document.body.appendChild(menu);

  menu.querySelector('[data-format="csv"]').addEventListener("click", () => {
    closeExportMenu();
    exportEquipoCsv(equipo);
  });
  menu.querySelector('[data-format="pdf"]').addEventListener("click", () => {
    closeExportMenu();
    exportEquipoPdf(equipo);
  });

  // Cerrar al hacer clic fuera.
  setTimeout(() => {
    document.addEventListener("click", function handler(event) {
      if (!menu.contains(event.target)) {
        closeExportMenu();
        document.removeEventListener("click", handler);
      }
    });
  }, 0);
}

// ---------- CSV por equipo ----------

async function exportEquipoCsv(equipo) {
  const nombre = equipo.nombreEquipo || "equipo";
  try {
    const detalle = await requestJson(`/api/equipos/${equipo.id}/detalle`);
    const eq = detalle?.equipo || {};
    const inv = detalle?.inventario;
    const discos = Array.isArray(detalle?.discos) ? detalle.discos : [];

    let software = [];
    try {
      software = await requestJson(`/api/equipos/${equipo.id}/software`);
      if (!Array.isArray(software)) software = [];
    } catch { /* sin software */ }

    let mantenimientos = [];
    try {
      mantenimientos = await requestJson(`/api/equipos/${equipo.id}/mantenimientos`);
      if (!Array.isArray(mantenimientos)) mantenimientos = [];
    } catch { /* sin mantenimientos */ }

    const rows = [];

    // Rellena etiquetas con espacios para que Excel no las corte en la columna A.
    const L = (label) => label.padEnd(24, " ");

    // Cabecera del documento
    rows.push([L("FICHA DE EQUIPO")]);
    rows.push([L("Equipo"), nombre]);
    rows.push([L("Responsable"), equipo.responsable || "—"]);
    rows.push([L("Fecha de exportación"), formatFullDate(new Date())]);
    rows.push([]);

    // Información general
    rows.push([L("INFORMACIÓN GENERAL")]);
    rows.push([L("Usuario"), eq.usuario || "—"]);
    rows.push([L("Dominio"), eq.dominio || "—"]);
    rows.push([L("Sistema operativo"), eq.sistemaOperativo || "—"]);
    rows.push([L("Versión del SO"), eq.versionSO || "—"]);
    rows.push([L("Dirección IP"), eq.direccionIP || "—"]);
    rows.push([L("Versión del agente"), eq.versionAgente || "—"]);
    rows.push([L("GUID"), eq.guid || "—"]);
    rows.push([]);

    // Hardware
    rows.push([L("HARDWARE")]);
    if (inv) {
      rows.push([L("Fabricante"), inv.fabricante || "—"]);
      rows.push([L("Modelo"), inv.modelo || "—"]);
      rows.push([L("Número de serie"), inv.numeroSerie || "—"]);
      rows.push([L("Procesador"), inv.procesador || "—"]);
      rows.push([L("Núcleos"), inv.nucleos || "—"]);
      rows.push([L("Memoria RAM"), `${inv.memoriaRamGB || 0} GB`]);
      rows.push([L("Arquitectura"), inv.arquitectura || "—"]);
      rows.push([L("Windows detectado"), inv.versionWindows || "—"]);
      rows.push([L("IP principal"), inv.ipPrincipal || "—"]);
      rows.push([L("MAC principal"), inv.macPrincipal || "—"]);
      rows.push([L("Último inventario"), formatFullDate(inv.fechaInventario)]);
    } else {
      rows.push([L("Sin inventario enviado")]);
    }
    rows.push([]);

    // Discos
    rows.push(["DISCOS"]);
    if (discos.length) {
      rows.push(["Unidad", "Etiqueta", "Archivos", "Tipo", "Total", "Libre", "Usado"]);
      for (const d of discos) {
        const total = Number(d.tamanoBytes || 0);
        const free = Number(d.libreBytes || 0);
        const used = total - free;
        rows.push([
          d.unidad || "—", d.etiqueta || "—", d.sistemaArchivos || "—",
          d.tipoUnidad || "—", formatBytes(total), formatBytes(free), formatBytes(used)
        ]);
      }
    } else {
      rows.push(["Sin datos de discos"]);
    }
    rows.push([]);

    // Software
    rows.push([`SOFTWARE INSTALADO (${software.length} programas)`]);
    if (software.length) {
      rows.push(["#", "Nombre", "Versión", "Fabricante", "Arquitectura", "Instalado"]);
      software.forEach((s, i) => {
        rows.push([
          i + 1, s.nombre || "—", s.version || "—", s.fabricante || "—",
          s.arquitectura || "—", formatDateOnly(s.fechaInstalacion)
        ]);
      });
    } else {
      rows.push(["Sin datos de software"]);
    }
    rows.push([]);

    // Mantenimientos
    rows.push([`MANTENIMIENTOS (${mantenimientos.length} registros)`]);
    if (mantenimientos.length) {
      rows.push(["#", "Estado", "Inicio", "Fin", "Acciones", "Mensaje", "Detalle técnico"]);
      mantenimientos.forEach((m, i) => {
        const acciones = [
          m.copiaOutlook && "Copia Outlook", m.mantenimiento && "Mantenimiento",
          m.reparacionWindows && "Reparación", m.apagarEquipo && "Apagar equipo"
        ].filter(Boolean).join(", ") || "—";
        rows.push([
          i + 1, m.estado || "—", formatDateTime(m.fechaInicio), formatDateTime(m.fechaFin),
          acciones, m.mensaje || "—", m.detalleTecnico || "—"
        ]);
      });
    } else {
      rows.push(["Sin mantenimientos registrados"]);
    }

    // Generar HTML con tabla que Excel abre con formato y ancho de columnas.
    const esc = (v) => String(v ?? "").replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
    const kv = (label, value) =>
      `<tr><td style="font-weight:bold;width:180px;background:#f0f4f8">${esc(label)}</td><td>${esc(value ?? "—")}</td></tr>`;
    const section = (title) =>
      `<tr><td colspan="2" style="background:#0f6cbd;color:#fff;font-weight:bold;font-size:13px;padding:6px 8px">${esc(title)}</td></tr>`;
    const tableHead = (cols) =>
      `<tr>${cols.map((c) => `<td style="background:#0f6cbd;color:#fff;font-weight:bold;padding:5px 8px">${esc(c)}</td>`).join("")}</tr>`;
    const tableRow = (cells) =>
      `<tr>${cells.map((c) => `<td style="padding:4px 8px;border-bottom:1px solid #e0e7ef">${esc(c)}</td>`).join("")}</tr>`;

    let html = `
      <html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
      <head><meta charset="utf-8"><!--[if gte mso 9]><xml><x:ExcelWorkbook><x:ExcelWorksheets><x:ExcelWorksheet>
      <x:Name>Ficha</x:Name><x:WorksheetOptions><x:DisplayGridlines/></x:WorksheetOptions>
      </x:ExcelWorksheet></x:ExcelWorksheets></x:ExcelWorkbook></xml><![endif]-->
      <style>
        body { font-family: Calibri, sans-serif; font-size: 11px; }
        td { vertical-align: top; }
      </style></head><body>
      <table border="0" cellpadding="0" cellspacing="0">`;

    // Cabecera
    html += section("FICHA DE EQUIPO");
    html += kv("Equipo", nombre);
    html += kv("Responsable", equipo.responsable);
    html += kv("Fecha de exportación", formatFullDate(new Date()));
    html += `<tr><td colspan="2" style="height:8px"></td></tr>`;

    // General
    html += section("INFORMACIÓN GENERAL");
    html += kv("Usuario", eq.usuario);
    html += kv("Dominio", eq.dominio);
    html += kv("Sistema operativo", eq.sistemaOperativo);
    html += kv("Versión del SO", eq.versionSO);
    html += kv("Dirección IP", eq.direccionIP);
    html += kv("Versión del agente", eq.versionAgente);
    html += kv("GUID", eq.guid);
    html += `<tr><td colspan="2" style="height:8px"></td></tr>`;

    // Hardware
    html += section("HARDWARE");
    if (inv) {
      html += kv("Fabricante", inv.fabricante);
      html += kv("Modelo", inv.modelo);
      html += kv("Número de serie", inv.numeroSerie);
      html += kv("Procesador", inv.procesador);
      html += kv("Núcleos", inv.nucleos);
      html += kv("Memoria RAM", `${inv.memoriaRamGB || 0} GB`);
      html += kv("Arquitectura", inv.arquitectura);
      html += kv("Windows detectado", inv.versionWindows);
      html += kv("IP principal", inv.ipPrincipal);
      html += kv("MAC principal", inv.macPrincipal);
      html += kv("Último inventario", formatFullDate(inv.fechaInventario));
    } else {
      html += `<tr><td colspan="2" style="padding:4px 8px;color:#888">Sin inventario enviado</td></tr>`;
    }
    html += `<tr><td colspan="2" style="height:8px"></td></tr>`;

    // Discos
    html += section("DISCOS");
    if (discos.length) {
      html += tableHead(["Unidad", "Etiqueta", "Archivos", "Tipo", "Total", "Libre", "Usado"]);
      for (const d of discos) {
        const total = Number(d.tamanoBytes || 0);
        const free = Number(d.libreBytes || 0);
        html += tableRow([d.unidad, d.etiqueta, d.sistemaArchivos, d.tipoUnidad, formatBytes(total), formatBytes(free), formatBytes(total - free)]);
      }
    } else {
      html += `<tr><td colspan="7" style="padding:4px 8px;color:#888">Sin datos de discos</td></tr>`;
    }
    html += `<tr><td colspan="7" style="height:8px"></td></tr>`;

    // Software
    html += section(`SOFTWARE INSTALADO (${software.length} programas)`);
    if (software.length) {
      html += tableHead(["#", "Nombre", "Versión", "Fabricante", "Arquitectura", "Instalado"]);
      software.forEach((s, i) => {
        html += tableRow([i + 1, s.nombre, s.version, s.fabricante, s.arquitectura, formatDateOnly(s.fechaInstalacion)]);
      });
    } else {
      html += `<tr><td colspan="6" style="padding:4px 8px;color:#888">Sin datos de software</td></tr>`;
    }
    html += `<tr><td colspan="6" style="height:8px"></td></tr>`;

    // Mantenimientos
    html += section(`MANTENIMIENTOS (${mantenimientos.length} registros)`);
    if (mantenimientos.length) {
      html += tableHead(["#", "Estado", "Inicio", "Fin", "Acciones", "Mensaje", "Detalle técnico"]);
      mantenimientos.forEach((m, i) => {
        const acciones = [
          m.copiaOutlook && "Copia Outlook", m.mantenimiento && "Mantenimiento",
          m.reparacionWindows && "Reparación", m.apagarEquipo && "Apagar equipo"
        ].filter(Boolean).join(", ") || "—";
        html += tableRow([i + 1, m.estado, formatDateTime(m.fechaInicio), formatDateTime(m.fechaFin), acciones, m.mensaje || "—", m.detalleTecnico || "—"]);
      });
    } else {
      html += `<tr><td colspan="7" style="padding:4px 8px;color:#888">Sin mantenimientos registrados</td></tr>`;
    }

    html += `</table></body></html>`;

    const blob = new Blob(["\uFEFF" + html], { type: "application/vnd.ms-excel;charset=utf-8" });
    downloadBlob(blob, `ficha_${nombre || equipo.id}_${fileTimestamp()}.xls`);
  } catch (error) {
    showAlert(`No se ha podido exportar: ${error.message}`);
  }
}

// ---------- PDF por equipo ----------

async function exportEquipoPdf(equipo) {
  if (typeof window.jspdf === "undefined") { showAlert("La librería PDF no está cargada. Recarga la página."); return; }
  const nombre = equipo.nombreEquipo || "equipo";

  try {
    const detalle = await requestJson(`/api/equipos/${equipo.id}/detalle`);
    const eq = detalle?.equipo || {};
    const inv = detalle?.inventario;
    const discos = Array.isArray(detalle?.discos) ? detalle.discos : [];

    let software = [];
    try {
      software = await requestJson(`/api/equipos/${equipo.id}/software`);
      if (!Array.isArray(software)) software = [];
    } catch { /* vacío */ }

    let mantenimientos = [];
    try {
      mantenimientos = await requestJson(`/api/equipos/${equipo.id}/mantenimientos`);
      if (!Array.isArray(mantenimientos)) mantenimientos = [];
    } catch { /* vacío */ }

    const { jsPDF } = window.jspdf;
    const doc = new jsPDF({ orientation: "portrait", unit: "mm", format: "a4" });
    const nombre = equipo.nombreEquipo || "Equipo sin nombre";
    let y = 0;

    // Encabezado
    doc.setFillColor(15, 108, 189);
    doc.rect(0, 0, 210, 28, "F");
    doc.setTextColor(255);
    doc.setFontSize(16);
    doc.setFont(undefined, "bold");
    doc.text(`Ficha: ${nombre}`, 14, 12);
    doc.setFontSize(9);
    doc.setFont(undefined, "normal");
    doc.text(`Tradipres Endpoint Manager · Generado: ${formatFullDate(new Date())}`, 14, 20);
    doc.setTextColor(0);
    y = 36;

    // Helper para títulos de sección
    const sectionTitle = (title) => {
      if (y > 260) { doc.addPage(); y = 20; }
      doc.setFontSize(12);
      doc.setFont(undefined, "bold");
      doc.setTextColor(15, 108, 189);
      doc.text(title, 14, y);
      y += 2;
      doc.setDrawColor(15, 108, 189);
      doc.line(14, y, 196, y);
      y += 6;
      doc.setTextColor(0);
    };

    // Helper para filas clave-valor
    const kvRows = (pairs) => {
      const data = pairs.filter(([, v]) => v !== null && v !== undefined && v !== "").map(([k, v]) => [k, String(v)]);
      if (!data.length) return;
      doc.autoTable({
        body: data,
        startY: y,
        theme: "plain",
        styles: { fontSize: 9, cellPadding: 1.5 },
        columnStyles: { 0: { fontStyle: "bold", cellWidth: 55 }, 1: { cellWidth: 130 } },
        margin: { left: 14, right: 14 }
      });
      y = doc.lastAutoTable.finalY + 5;
    };

    // Sección: General
    sectionTitle("Información general");
    kvRows([
      ["Responsable", equipo.responsable],
      ["Usuario", eq.usuario],
      ["Dominio", eq.dominio],
      ["Sistema operativo", eq.sistemaOperativo],
      ["Versión del SO", eq.versionSO],
      ["Dirección IP", eq.direccionIP],
      ["Versión del agente", eq.versionAgente],
      ["GUID", eq.guid]
    ]);

    // Sección: Hardware
    sectionTitle("Hardware e inventario");
    if (inv) {
      kvRows([
        ["Fabricante", inv.fabricante],
        ["Modelo", inv.modelo],
        ["Número de serie", inv.numeroSerie],
        ["Procesador", inv.procesador],
        ["Núcleos", inv.nucleos],
        ["Memoria RAM", `${formatNumber(inv.memoriaRamGB, 2)} GB`],
        ["Arquitectura", inv.arquitectura],
        ["Windows detectado", inv.versionWindows],
        ["IP principal", inv.ipPrincipal],
        ["MAC principal", inv.macPrincipal],
        ["Último inventario", formatFullDate(inv.fechaInventario)]
      ]);
    } else {
      doc.setFontSize(9);
      doc.setTextColor(100);
      doc.text("Este equipo todavía no ha enviado inventario.", 14, y);
      doc.setTextColor(0);
      y += 8;
    }

    // Sección: Discos
    sectionTitle("Discos");
    if (discos.length) {
      doc.autoTable({
        head: [["Unidad", "Etiqueta", "Archivos", "Tipo", "Total", "Libre", "Usado %"]],
        body: discos.map((d) => {
          const total = Number(d.tamanoBytes || 0);
          const free = Number(d.libreBytes || 0);
          const usedPct = total > 0 ? (((total - free) / total) * 100).toFixed(1) : "0";
          return [d.unidad || "", d.etiqueta || "", d.sistemaArchivos || "", d.tipoUnidad || "", formatBytes(total), formatBytes(free), `${usedPct}%`];
        }),
        startY: y,
        styles: { fontSize: 8, cellPadding: 2 },
        headStyles: { fillColor: [15, 108, 189], textColor: 255 },
        margin: { left: 14, right: 14 }
      });
      y = doc.lastAutoTable.finalY + 5;
    } else {
      doc.setFontSize(9);
      doc.setTextColor(100);
      doc.text("No hay información de discos.", 14, y);
      doc.setTextColor(0);
      y += 8;
    }

    // Sección: Software (todos los programas)
    sectionTitle(`Software instalado (${software.length} programas)`);
    if (software.length) {
      doc.autoTable({
        head: [["#", "Nombre", "Versión", "Fabricante", "Arquitectura"]],
        body: software.map((s, i) => [i + 1, s.nombre || "", s.version || "", s.fabricante || "", s.arquitectura || ""]),
        startY: y,
        styles: { fontSize: 7.5, cellPadding: 1.5 },
        headStyles: { fillColor: [15, 108, 189], textColor: 255 },
        columnStyles: { 0: { cellWidth: 10 }, 1: { cellWidth: 60 }, 2: { cellWidth: 22 }, 3: { cellWidth: 55 } },
        margin: { left: 14, right: 14 }
      });
      y = doc.lastAutoTable.finalY + 3;
    } else {
      doc.setFontSize(9);
      doc.setTextColor(100);
      doc.text("Sin datos de software.", 14, y);
      doc.setTextColor(0);
      y += 8;
    }

    // Sección: Mantenimientos
    sectionTitle("Mantenimientos");
    if (mantenimientos.length) {
      doc.autoTable({
        head: [["Estado", "Inicio", "Fin", "Acciones", "Mensaje", "Detalle técnico"]],
        body: mantenimientos.map((m) => {
          const acciones = [
            m.copiaOutlook && "Outlook", m.mantenimiento && "Mantenimiento",
            m.reparacionWindows && "Windows", m.apagarEquipo && "Apagar"
          ].filter(Boolean).join(", ");
          return [m.estado || "", formatDateTime(m.fechaInicio), formatDateTime(m.fechaFin), acciones, m.mensaje || "", m.detalleTecnico || ""];
        }),
        startY: y,
        styles: { fontSize: 7, cellPadding: 1.2 },
        headStyles: { fillColor: [15, 108, 189], textColor: 255 },
        columnStyles: { 0: { cellWidth: 18 }, 1: { cellWidth: 28 }, 2: { cellWidth: 28 }, 3: { cellWidth: 28 }, 4: { cellWidth: 35 }, 5: { cellWidth: 45 } },
        margin: { left: 14, right: 14 }
      });
    } else {
      doc.setFontSize(9);
      doc.setTextColor(100);
      doc.text("Sin mantenimientos registrados.", 14, y);
      doc.setTextColor(0);
    }

    doc.save(`ficha_${nombre || equipo.id}_${fileTimestamp()}.pdf`);
  } catch (error) {
    showAlert(`No se ha podido exportar el PDF: ${error.message}`);
  }
}

// ============ Eventos e inicialización ============

// Refresco manual, búsqueda y filtro de la tabla de inventario.
elements.refreshButton.addEventListener("click", () => refreshInventory());
elements.exportCsvButton.addEventListener("click", exportGeneralCsv);
elements.exportPdfButton.addEventListener("click", exportGeneralPdf);
elements.searchInput.addEventListener("input", renderTable);
elements.stateFilter.addEventListener("change", renderTable);
// Cierre del diálogo de ficha (botón X o clic fuera).
elements.closeDialog.addEventListener("click", () => elements.detailsDialog.close());
elements.detailsDialog.addEventListener("click", (event) => {
  if (event.target === elements.detailsDialog) elements.detailsDialog.close();
});
// Cambio de pestaña de la ficha (dispara cargas diferidas).
for (const tab of elements.detailTabs) {
  tab.addEventListener("click", () => activateDetailTab(tab.dataset.tab));
}
// Buscador de software dentro de la ficha.
elements.softwareSearch.addEventListener("input", renderSoftware);
// Formulario de programación de mantenimientos.
elements.scheduleButton.addEventListener("click", showScheduleForm);
elements.scheduleCancel.addEventListener("click", hideScheduleForm);
elements.scheduleForm.addEventListener("submit", submitSchedule);
elements.closeCancel.addEventListener("click", hideCloseForm);
elements.closeForm.addEventListener("submit", submitCloseForm);
// Formulario de tareas avanzadas (PowerShell).
elements.advancedTaskButton.addEventListener("click", showAdvancedTaskForm);
elements.advancedTaskCancel.addEventListener("click", hideAdvancedTaskForm);
elements.advancedTaskForm.addEventListener("submit", submitAdvancedTask);
// Cambio entre tipo Único y Diario (muestra/oculta días y fecha de fin).
for (const radio of elements.scheduleTypeRadios) {
  radio.addEventListener("change", applyScheduleType);
}
// Habilitar/deshabilitar el campo de fecha de fin del Diario.
elements.scheduleEndEnabled.addEventListener("change", applyEndEnabled);
// Repintar la lista de prioridad al marcar/desmarcar acciones.
for (const flag of [elements.flagOutlook, elements.flagMaintenance, elements.flagWindows, elements.flagShutdown]) {
  flag.addEventListener("change", renderActionOrder);
}

// ============ Tiempo real (SignalR) ============

// Compara dos GUID sin importar mayúsculas, llaves ni espacios.
function sameGuid(a, b) {
  const normalizar = (valor) =>
    String(valor || "").replace(/[{}\s]/g, "").toLocaleLowerCase("es-ES");
  const guid = normalizar(a);
  return guid !== "" && guid === normalizar(b);
}

// Cambio de estado de un mantenimiento: si afecta al equipo abierto en la
// ficha, se recargan sus mantenimientos para reflejarlo al instante.
document.addEventListener("realtime:mantenimiento", (evento) => {
  const cambio = evento.detail || {};
  const equipo = state.selectedEquipo;
  if (!equipo || !sameGuid(equipo.guid, cambio.equipoGuid)) return;

  state.mantenimientosLoadedFor = null;
  if (elements.detailsDialog.open) {
    loadMaintenanceForSelectedEquipment();
  }
});

// Cambio de estado de una tarea avanzada: si afecta al equipo abierto en la
// ficha, se recarga su pestaña de tareas sin recargar la página.
document.addEventListener("realtime:tareaAvanzada", (evento) => {
  const cambio = evento.detail || {};
  const equipo = state.selectedEquipo;
  if (!equipo || !sameGuid(equipo.guid, cambio.equipoGuid)) return;

  state.advancedTasksLoadedFor = null;
  if (elements.detailsDialog.open) {
    loadAdvancedTasksForSelectedEquipment();
  }
});

// Heartbeat de un equipo: se pinta como Online al instante (el paso a Offline
// sigue siendo por silencio, lo cubre el refresco normal de la tabla).
document.addEventListener("realtime:heartbeat", (evento) => {
  const latido = evento.detail || {};
  if (!latido.equipoGuid) return;

  let cambio = false;
  for (const equipo of state.equipos) {
    if (sameGuid(equipo.guid, latido.equipoGuid)) {
      if (!isOnline(equipo)) {
        equipo.estado = "online";
        cambio = true;
      }
      break;
    }
  }

  if (cambio) {
    renderSummary();
    renderTable();
  }

  if (state.selectedEquipo && sameGuid(state.selectedEquipo.guid, latido.equipoGuid)) {
    state.selectedEquipo.estado = "online";
    elements.detailStatus.innerHTML = `<span class="status-pill online">Online</span>`;
  }
});

// ============ Refresco periódico de seguridad (cada 10 s) ============
// Repasa el estado (Online/Offline e inventario) de todos los equipos y, si la
// ficha está abierta con su pestaña de mantenimiento cargada, actualiza también
// esos mantenimientos. Complementa a los avisos por socket.
async function refrescoPeriodico() {
  await refreshInventory({ silent: true });

  if (!elements.detailsDialog.open) return;
  const equipo = state.selectedEquipo;
  if (!equipo) return;

  // No pisar formularios que el usuario tenga abiertos.
  const formularioAbierto =
    !elements.scheduleForm.classList.contains("hidden") ||
    !elements.advancedTaskForm.classList.contains("hidden") ||
    state.closingMaintenanceId !== null ||
    state.editingDiarioId !== null;
  if (formularioAbierto) return;

  if (state.mantenimientosLoadedFor === equipo.id) {
    try {
      const mantenimientos = await requestJson(`/api/equipos/${equipo.id}/mantenimientos`);
      renderMaintenanceList(Array.isArray(mantenimientos) ? mantenimientos : []);
    } catch {
      // Silencioso: si falla, el aviso por socket o el refresco manual lo cubren.
    }
  }

  if (state.advancedTasksLoadedFor === equipo.id) {
    try {
      const tareas = await requestJson(`/api/equipos/${equipo.id}/advanced-tasks`);
      renderAdvancedTasks(Array.isArray(tareas) ? tareas : []);
    } catch {
      // Silencioso: igual que los mantenimientos.
    }
  }
}

window.setInterval(refrescoPeriodico, 10000);

// Carga inicial del inventario al abrir la página.
refreshInventory();
