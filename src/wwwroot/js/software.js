"use strict";

/*
 * software.js — Lógica de la página /Software/ (inventario de software).
 *
 * Funcionamiento:
 *  1. GET /api/software/resumen → programas agregados por nombre+versión+
 *     fabricante+arquitectura, con el número de equipos donde aparece.
 *  2. GET /api/equipos → total de equipos (para la tarjeta "sin software").
 *  3. Barra de etiquetas: un chip por fabricante con su contador; se pueden
 *     seleccionar varias a la vez (filtro en O) y se combinan con la búsqueda.
 *  4. Tabla agrupada por fabricante (grupos plegables con clic); el botón
 *     "Agrupar por fabricante" alterna entre vista agrupada y lista plana.
 *  5. El botón con el nº de equipos de cada programa abre un diálogo con las
 *     máquinas donde está instalado (GET /api/software/grupo).
 */

// Límite de filas visibles en la tabla (por rendimiento).
const MAX_ROWS = 500;

// ============ Estado en memoria y referencias al DOM ============

// tags: etiquetas derivadas de los fabricantes; selectedTags: Set de claves
// activas; collapsedGroups: grupos plegados (sobreviven al redibujado).
const state = {
  resumen: null,
  refreshing: false,
  tags: [],
  selectedTags: new Set(),
  grouped: true,
  collapsedGroups: new Set()
};

// Acceso directo a los elementos del HTML (página + diálogo de equipos).
const elements = {
  refreshButton: document.querySelector("#refreshButton"),
  lastUpdate: document.querySelector("#lastUpdate"),
  alertBox: document.querySelector("#alertBox"),
  softwareSearch: document.querySelector("#softwareSearch"),
  groupToggle: document.querySelector("#groupToggle"),
  tagBar: document.querySelector("#tagBar"),
  tableBody: document.querySelector("#softwareTableBody"),
  tableNote: document.querySelector("#tableNote"),
  emptyState: document.querySelector("#emptyState"),
  programCount: document.querySelector("#programCount"),
  installCount: document.querySelector("#installCount"),
  machineCount: document.querySelector("#machineCount"),
  missingCount: document.querySelector("#missingCount"),
  machinesDialog: document.querySelector("#machinesDialog"),
  closeMachinesDialog: document.querySelector("#closeMachinesDialog"),
  machinesProgramName: document.querySelector("#machinesProgramName"),
  machinesLoading: document.querySelector("#machinesLoading"),
  machinesList: document.querySelector("#machinesList")
};

// ============ Comunicación con la API ============

// GET JSON sin caché; lanza Error con el campo "error" del servidor si falla.
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
    throw new Error(payload?.error || `Error HTTP ${response.status}`);
  }

  return payload;
}

// ============ Refresco del inventario ============

/*
 * Descarga el resumen de software y el total de equipos, actualiza las
 * tarjetas de resumen, reconstruye las etiquetas de fabricante y repinta
 * la tabla.
 */
async function refreshSoftware() {
  if (state.refreshing) return;

  state.refreshing = true;
  elements.refreshButton.disabled = true;
  elements.refreshButton.textContent = "Actualizando…";
  hideAlert();

  try {
    const resumen = await requestJson("/api/software/resumen");
    state.resumen = {
      programas: resumen.programas || 0,
      instalaciones: Number(resumen.instalaciones) || 0,
      equiposConSoftware: resumen.equiposConSoftware || 0,
      items: Array.isArray(resumen.items) ? resumen.items : []
    };

    const equipos = await requestJson("/api/equipos");
    const totalEquipos = Array.isArray(equipos) ? equipos.length : 0;

    elements.programCount.textContent = state.resumen.programas;
    elements.installCount.textContent = formatNumber(state.resumen.instalaciones);
    elements.machineCount.textContent = state.resumen.equiposConSoftware;
    elements.missingCount.textContent = Math.max(
      0,
      totalEquipos - state.resumen.equiposConSoftware
    );

    buildTags();
    renderTags();
    renderTable();
    elements.lastUpdate.textContent = new Intl.DateTimeFormat("es-ES", {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit"
    }).format(new Date());
  } catch (error) {
    showAlert(`No se ha podido cargar el inventario de software: ${error.message}`);
  } finally {
    state.refreshing = false;
    elements.refreshButton.disabled = false;
    elements.refreshButton.textContent = "Actualizar";
  }
}

// ============ Etiquetas por fabricante ============

// Clave de agrupación: nombre del fabricante limpio, o "Sin fabricante".
function tagKey(fabricante) {
  const value = String(fabricante || "").trim();
  return value || "Sin fabricante";
}

// Cuenta programas por fabricante y descarta etiquetas ya desaparecidas.
function buildTags() {
  const items = state.resumen?.items || [];
  const counts = new Map();

  for (const item of items) {
    const key = tagKey(item.fabricante);
    counts.set(key, (counts.get(key) || 0) + 1);
  }

  state.tags = [...counts.entries()]
    .map(([label, count]) => ({ key: label, label, count }))
    .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label, "es"));

  for (const key of [...state.selectedTags]) {
    if (!counts.has(key)) state.selectedTags.delete(key);
  }
}

// Pinta la barra de chips: "Todas" + una etiqueta por fabricante con contador.
function renderTags() {
  const total = state.resumen?.items?.length || 0;
  const selected = state.selectedTags;

  elements.tagBar.innerHTML = `
    <span class="tag-bar-label">Etiquetas</span>
    <button class="tag-chip ${selected.size === 0 ? "active" : ""}" type="button" data-tag="__all__">
      Todas <span class="tag-count">${formatNumber(total)}</span>
    </button>
    ${state.tags.map((tag) => `
      <button class="tag-chip ${selected.has(tag.key) ? "active" : ""}" type="button" data-tag="${escapeHtml(tag.key)}">
        ${escapeHtml(tag.label)} <span class="tag-count">${formatNumber(tag.count)}</span>
      </button>
    `).join("")}
  `;
}

// Selecciona/deselecciona una etiqueta ("__all__" limpia la selección).
function toggleTag(tag) {
  if (tag === "__all__") {
    state.selectedTags.clear();
  } else if (state.selectedTags.has(tag)) {
    state.selectedTags.delete(tag);
  } else {
    state.selectedTags.add(tag);
  }

  renderTags();
  renderTable();
}

// Pliega/despliega un grupo de la tabla (por nombre de fabricante).
function toggleGroup(group) {
  if (state.collapsedGroups.has(group)) {
    state.collapsedGroups.delete(group);
  } else {
    state.collapsedGroups.add(group);
  }

  renderTable();
}

// ============ Tabla de software ============

/*
 * Aplica los tres filtros (búsqueda de texto, etiquetas seleccionadas y
 * modo agrupado) y repinta la tabla; muestra la nota con el nº de filas
 * y, si procede, el aviso del tope de MAX_ROWS.
 */
function renderTable() {
  const items = state.resumen?.items || [];
  const query = elements.softwareSearch.value.trim().toLocaleLowerCase("es-ES");
  const selected = state.selectedTags;

  const filtered = items.filter((item) => {
    if (selected.size > 0 && !selected.has(tagKey(item.fabricante))) return false;
    const searchable = [item.nombre, item.version, item.fabricante, item.arquitectura]
      .filter(Boolean).join(" ").toLocaleLowerCase("es-ES");
    return !query || searchable.includes(query);
  });

  elements.tableBody.innerHTML = "";
  elements.emptyState.classList.toggle("hidden", filtered.length !== 0);
  document.querySelector(".table-wrap").classList.toggle("hidden", filtered.length === 0);

  if (filtered.length > MAX_ROWS) {
    elements.tableNote.textContent =
      `Mostrando los ${MAX_ROWS} primeros de ${formatNumber(filtered.length)} programas. Afina la búsqueda o selecciona etiquetas para ver el resto.`;
    elements.tableNote.classList.remove("hidden");
  } else if (filtered.length > 0) {
    let text = `${formatNumber(filtered.length)} ${filtered.length === 1 ? "programa" : "programas"}`;
    if (state.grouped) {
      const groups = countGroups(filtered);
      text += ` en ${groups} ${groups === 1 ? "grupo" : "grupos"}`;
    }
    elements.tableNote.textContent = `${text}.`;
    elements.tableNote.classList.remove("hidden");
  } else {
    elements.tableNote.classList.add("hidden");
  }

  if (filtered.length === 0) return;

  if (state.grouped) {
    renderGroupedRows(filtered);
  } else {
    renderFlatRows(filtered.slice(0, MAX_ROWS));
  }
}

// Nº de fabricantes distintos presentes en la lista filtrada.
function countGroups(items) {
  return new Set(items.map((item) => tagKey(item.fabricante))).size;
}

/*
 * Vista agrupada: cabecera de fila por fabricante (con contador y chevron)
 * seguida de sus programas ordenados alfabéticamente. Los grupos van de
 * mayor a menor; al plegar uno solo se pinta su cabecera.
 */
function renderGroupedRows(filtered) {
  const groups = new Map();

  for (const item of filtered) {
    const key = tagKey(item.fabricante);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(item);
  }

  const ordered = [...groups.entries()].sort(
    (a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0], "es")
  );

  let rendered = 0;

  for (const [label, groupItems] of ordered) {
    groupItems.sort((a, b) =>
      String(a.nombre || "").localeCompare(String(b.nombre || ""), "es")
    );

    const collapsed = state.collapsedGroups.has(label);
    const total = groupItems.length;
    const remaining = MAX_ROWS - rendered;
    const visible = collapsed ? [] : groupItems.slice(0, Math.max(0, remaining));
    const parcial = !collapsed && visible.length < total;
    const cantidad = parcial ? `${formatNumber(visible.length)} de ${formatNumber(total)}` : formatNumber(total);

    elements.tableBody.insertAdjacentHTML("beforeend", `
      <tr class="group-row${collapsed ? " collapsed" : ""}" data-group="${escapeHtml(label)}" tabindex="0">
        <th colspan="5">
          <span class="group-head">
            <span class="group-chevron">${collapsed ? "▸" : "▾"}</span>
            <span class="group-name">${escapeHtml(label)}</span>
            <span class="group-count">${cantidad} ${total === 1 ? "programa" : "programas"}</span>
          </span>
        </th>
      </tr>
      ${visible.map(renderRow).join("")}
    `);

    rendered += visible.length;
    if (rendered >= MAX_ROWS) break;
  }

  bindRowButtons();

  for (const header of elements.tableBody.querySelectorAll(".group-row")) {
    const activate = () => toggleGroup(header.dataset.group);
    header.addEventListener("click", activate);
    header.addEventListener("keydown", (event) => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        activate();
      }
    });
  }
}

// Vista plana: filas sueltas sin cabeceras de grupo (recortadas a MAX_ROWS).
function renderFlatRows(visible) {
  elements.tableBody.insertAdjacentHTML("beforeend",
    visible.map(renderRow).join("")
  );
  bindRowButtons();
}

// HTML de una fila de programa (el botón de equipos lleva los datos en data-*).
function renderRow(item) {
  return `
    <tr>
      <td><span class="computer-name">${escapeHtml(item.nombre || "Sin nombre")}</span></td>
      <td>${escapeHtml(item.version || "—")}</td>
      <td>${escapeHtml(item.fabricante || "—")}</td>
      <td>${escapeHtml(item.arquitectura || "—")}</td>
      <td>
        <button class="details-button machines-button" type="button"
          data-nombre="${escapeHtml(item.nombre || "")}"
          data-version="${escapeHtml(item.version || "")}"
          data-fabricante="${escapeHtml(item.fabricante || "")}"
          data-arquitectura="${escapeHtml(item.arquitectura || "")}">
          ${formatNumber(item.equipos)} ${item.equipos === 1 ? "equipo" : "equipos"}
        </button>
      </td>
    </tr>
  `;
}

// Asocia el clic de cada botón "N equipos" al diálogo de máquinas.
function bindRowButtons() {
  for (const button of elements.tableBody.querySelectorAll(".machines-button")) {
    button.addEventListener("click", () => showMachines({
      nombre: button.dataset.nombre,
      version: button.dataset.version,
      fabricante: button.dataset.fabricante,
      arquitectura: button.dataset.arquitectura
    }));
  }
}

// ============ Diálogo "equipos con este programa" ============

/*
 * Abre el diálogo y pide GET /api/software/grupo con los 4 criterios del
 * programa (nombre, versión, fabricante y arquitectura) para listar las
 * máquinas donde está instalado, con su estado online/offline.
 */
async function showMachines(item) {
  elements.machinesProgramName.textContent =
    [item.nombre, item.version].filter(Boolean).join(" — ") || "Programa";
  elements.machinesList.innerHTML = "";
  elements.machinesLoading.classList.remove("hidden");
  elements.machinesDialog.showModal();

  try {
    const query = new URLSearchParams({
      nombre: item.nombre || "",
      version: item.version || "",
      fabricante: item.fabricante || "",
      arquitectura: item.arquitectura || ""
    });

    const equipos = await requestJson(`/api/software/grupo?${query.toString()}`);

    if (!Array.isArray(equipos) || equipos.length === 0) {
      elements.machinesList.innerHTML =
        `<div class="detail-empty">No se han encontrado equipos con este programa.</div>`;
      return;
    }

    elements.machinesList.innerHTML = equipos.map((equipo) => {
      const online = String(equipo.estado || "").toLocaleLowerCase("es-ES") === "online";
      return `
        <div class="machine-item">
          <div>
            <strong>${escapeHtml(equipo.nombreEquipo || "Sin nombre")}</strong>
            <span>${escapeHtml(equipo.dominio || "Sin dominio")} · ${escapeHtml(equipo.direccionIP || "Sin IP")}</span>
          </div>
          <div class="machine-meta">
            <span class="status-pill ${online ? "online" : "offline"}">${online ? "Online" : "Offline"}</span>
          </div>
        </div>
      `;
    }).join("");
  } catch (error) {
    elements.machinesList.innerHTML =
      `<div class="detail-empty">No se ha podido cargar la lista de equipos: ${escapeHtml(error.message)}</div>`;
  } finally {
    elements.machinesLoading.classList.add("hidden");
  }
}

// ============ Utilidades ============

// Número en formato español ("1.234").
function formatNumber(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) return "—";
  return new Intl.NumberFormat("es-ES").format(number);
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

// Muestra/oculta la alerta superior de la página.
function showAlert(message) {
  elements.alertBox.textContent = message;
  elements.alertBox.classList.remove("hidden");
}

function hideAlert() {
  elements.alertBox.classList.add("hidden");
  elements.alertBox.textContent = "";
}

// ============ Eventos e inicialización ============

// Refresco manual y búsqueda de texto.
elements.refreshButton.addEventListener("click", refreshSoftware);
elements.softwareSearch.addEventListener("input", renderTable);
// Clic en una etiqueta (delegación en la barra de chips).
elements.tagBar.addEventListener("click", (event) => {
  const chip = event.target.closest(".tag-chip");
  if (chip) toggleTag(chip.dataset.tag);
});
// Alternar vista agrupada / plana.
elements.groupToggle.addEventListener("click", () => {
  state.grouped = !state.grouped;
  elements.groupToggle.classList.toggle("active", state.grouped);
  elements.groupToggle.setAttribute("aria-pressed", String(state.grouped));
  renderTable();
});
// Cierre del diálogo de equipos (botón X o clic fuera).
elements.closeMachinesDialog.addEventListener("click", () => elements.machinesDialog.close());
elements.machinesDialog.addEventListener("click", (event) => {
  if (event.target === elements.machinesDialog) elements.machinesDialog.close();
});

// Carga inicial del inventario de software.
refreshSoftware();
