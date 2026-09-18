// Helper functions for safe string operations and type extraction
function getShortName(str) {
    if (!str) return 'Unknown';
    const idx = str.lastIndexOf('.');
    return idx === -1 ? str : str.substring(idx + 1);
}

function getParamType(p) {
    if (!p) return 'Unknown';
    return p.ParameterType || p.parameter_type || p.parameterType || p.FullName || p.fullName || p.Type || p.type || 'Unknown';
}

function getParamName(p) {
    if (!p) return '';
    return p.Name || p.name || '';
}

// State variables
const state = {
    activeObjectId: null,
    activeObjectName: '',
    expandedComponents: new Set(),
    expandedNodes: new Set(),
    screenshotScale: 0.5,
    autoRefreshInterval: null,
    currentTheme: 'dark',
    componentsDetails: {} // Cache for component details
};

// JSON-RPC Tool Caller
async function callTool(name, args = {}) {
    try {
        const response = await fetch('/mcp', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                jsonrpc: '2.0',
                method: 'tools/call',
                params: {
                    name: name,
                    arguments: args
                },
                id: Date.now()
            })
        });
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        const data = await response.json();
        if (data.error) {
            throw new Error(data.error.message);
        }
        
        if (data.result && data.result.content && data.result.content[0]) {
            const textContent = data.result.content[0].text;
            return JSON.parse(textContent);
        }
        
        return data.result;
    } catch (err) {
        console.error(`Failed to execute tool ${name}:`, err);
        throw err;
    }
}

// Page Initialization
document.addEventListener('DOMContentLoaded', () => {
    initTheme();
    initTabs();
    initEventListeners();
    
    // Initial Load
    loadRootHierarchy();
    refreshScreenshot();
});

// Theme Logic (Dark/Light)
function initTheme() {
    const themeToggleBtn = document.getElementById('theme-toggle');
    const themeIcon = document.getElementById('theme-icon');
    
    // Check local storage or system preference
    const savedTheme = localStorage.getItem('theme');
    const prefersLight = window.matchMedia('(prefers-color-scheme: light)').matches;
    
    if (savedTheme === 'light' || (!savedTheme && prefersLight)) {
        setTheme('light');
    } else {
        setTheme('dark');
    }
    
    themeToggleBtn.addEventListener('click', () => {
        if (state.currentTheme === 'dark') {
            setTheme('light');
        } else {
            setTheme('dark');
        }
    });
}

function setTheme(theme) {
    state.currentTheme = theme;
    localStorage.setItem('theme', theme);
    
    const root = document.documentElement;
    const themeIcon = document.getElementById('theme-icon');
    
    if (theme === 'light') {
        root.classList.add('light-theme');
        root.classList.remove('dark-theme');
        themeIcon.src = 'assets/sun.svg';
    } else {
        root.classList.remove('light-theme');
        root.classList.add('dark-theme');
        themeIcon.src = 'assets/moon.svg';
    }
}

// Sidebar Tabs Logic
function initTabs() {
    const tabHierarchy = document.getElementById('tab-hierarchy');
    const tabSearch = document.getElementById('tab-search');
    const panelHierarchy = document.getElementById('panel-hierarchy');
    const panelSearch = document.getElementById('panel-search');
    
    tabHierarchy.addEventListener('click', () => {
        tabHierarchy.classList.add('active');
        tabSearch.classList.remove('active');
        panelHierarchy.classList.add('active');
        panelSearch.classList.remove('active');
    });
    
    tabSearch.addEventListener('click', () => {
        tabSearch.classList.add('active');
        tabHierarchy.classList.remove('active');
        panelSearch.classList.add('active');
        panelHierarchy.classList.remove('active');
    });
}

// Event Listeners
function initEventListeners() {
    // Hierarchy Reload
    document.getElementById('btn-refresh-hierarchy').addEventListener('click', loadRootHierarchy);
    
    // Search Action
    document.getElementById('btn-run-search').addEventListener('click', executeSearch);
    document.getElementById('input-search-class').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') executeSearch();
    });
    
    // Screenshot Actions
    document.getElementById('btn-refresh-screenshot').addEventListener('click', refreshScreenshot);
    
    const sliderScale = document.getElementById('slider-scale');
    const scaleVal = document.getElementById('scale-val');
    sliderScale.addEventListener('input', (e) => {
        state.screenshotScale = parseFloat(e.target.value);
        scaleVal.textContent = state.screenshotScale.toFixed(2);
    });
    sliderScale.addEventListener('change', refreshScreenshot);
    
    const chkAutoRefresh = document.getElementById('chk-auto-refresh');
    const selectRefreshRate = document.getElementById('select-refresh-rate');
    
    chkAutoRefresh.addEventListener('change', () => {
        toggleAutoRefresh(chkAutoRefresh.checked, parseInt(selectRefreshRate.value, 10));
    });
    
    selectRefreshRate.addEventListener('change', () => {
        if (chkAutoRefresh.checked) {
            toggleAutoRefresh(true, parseInt(selectRefreshRate.value, 10));
        }
    });

    // Object Active State Toggle
    document.getElementById('inspect-obj-active').addEventListener('change', async (e) => {
        if (!state.activeObjectId) return;
        try {
            await callTool('write_field', {
                instance_id: state.activeObjectId,
                name: 'activeSelf',
                value: e.target.checked ? 'true' : 'false'
            });
            // Update node style in hierarchy if currently loaded
            const labelNode = document.querySelector(`[data-node-id="${state.activeObjectId}"] > .tree-label`);
            if (labelNode) {
                if (e.target.checked) {
                    labelNode.style.opacity = '1';
                } else {
                    labelNode.style.opacity = '0.5';
                }
            }
        } catch (err) {
            alert(`Failed to set active state: ${err.message}`);
            e.target.checked = !e.target.checked; // Revert
        }
    });

    // Modal Close buttons
    document.getElementById('btn-close-modal').addEventListener('click', closeModal);
    document.getElementById('btn-modal-cancel').addEventListener('click', closeModal);

    // Search mode selection changed listener
    document.querySelectorAll('input[name="search-mode"]').forEach(radio => {
        radio.addEventListener('change', (e) => {
            const assetsContainer = document.getElementById('search-option-assets-container');
            if (e.target.value === 'instances') {
                assetsContainer.classList.remove('hidden');
            } else {
                assetsContainer.classList.add('hidden');
            }
        });
    });
}

// Hierarchy Loader (Root)
async function loadRootHierarchy() {
    const container = document.getElementById('hierarchy-tree');
    container.innerHTML = '<div class="loading">Loading scenes...</div>';
    
    try {
        const rootData = await callTool('get_hierarchy', { instance_id: 0 });
        container.innerHTML = '';
        
        if (rootData && rootData.children && rootData.children.length > 0) {
            rootData.children.forEach(child => {
                renderNode(child, container);
            });
        } else {
            container.innerHTML = '<div class="empty-state">No root GameObjects found.</div>';
        }
    } catch (err) {
        container.innerHTML = `<div class="empty-state" style="color:var(--accent-danger)">Error loading hierarchy: ${err.message}</div>`;
    }
}

// Render Single Node in Tree
function renderNode(nodeInfo, parentElement) {
    if (!nodeInfo) return;
    
    const nodeId = nodeInfo.instance_id;
    const nodeWrapper = document.createElement('div');
    nodeWrapper.className = 'tree-node';
    nodeWrapper.dataset.nodeId = nodeId;
    
    const content = document.createElement('div');
    content.className = 'tree-node-content';
    if (state.activeObjectId === nodeId) {
        content.classList.add('selected');
    }
    
    const arrow = document.createElement('span');
    arrow.className = 'tree-arrow';
    arrow.textContent = '▶';
    
    const label = document.createElement('span');
    label.className = 'tree-label';
    label.textContent = nodeInfo.name || 'GameObject';
    
    content.appendChild(arrow);
    content.appendChild(label);
    nodeWrapper.appendChild(content);
    
    const childrenContainer = document.createElement('div');
    childrenContainer.className = 'tree-node-children hidden';
    nodeWrapper.appendChild(childrenContainer);
    
    parentElement.appendChild(nodeWrapper);
    
    // Toggle expand/collapse or select
    arrow.addEventListener('click', async (e) => {
        e.stopPropagation();
        
        if (state.expandedNodes.has(nodeId)) {
            // Collapse
            state.expandedNodes.delete(nodeId);
            arrow.classList.remove('expanded');
            childrenContainer.classList.add('hidden');
        } else {
            // Expand
            state.expandedNodes.add(nodeId);
            arrow.classList.add('expanded');
            childrenContainer.classList.remove('hidden');
            
            if (childrenContainer.children.length === 0) {
                childrenContainer.innerHTML = '<div class="loading">Loading...</div>';
                try {
                    const data = await callTool('get_hierarchy', { instance_id: nodeId });
                    childrenContainer.innerHTML = '';
                    
                    if (data && data.children && data.children.length > 0) {
                        data.children.forEach(child => {
                            renderNode(child, childrenContainer);
                        });
                    } else {
                        arrow.classList.add('empty');
                        arrow.textContent = '•';
                        childrenContainer.innerHTML = '';
                    }
                } catch (err) {
                    childrenContainer.innerHTML = `<div class="error" style="color:var(--accent-danger); font-size:11px; padding:4px 12px;">Error: ${err.message}</div>`;
                }
            }
        }
    });
    
    content.addEventListener('click', () => {
        // Clear previous selected styling
        document.querySelectorAll('.tree-node-content').forEach(el => el.classList.remove('selected'));
        content.classList.add('selected');
        
        selectObject(nodeId, nodeInfo.name);
    });
}

// Select GameObject & Load its Inspector panel
async function selectObject(id, name) {
    state.activeObjectId = id;
    state.activeObjectName = name;
    state.expandedComponents.clear();
    state.componentsDetails = {};
    
    const emptyPanel = document.getElementById('inspector-empty');
    const contentPanel = document.getElementById('inspector-content');
    
    emptyPanel.classList.add('hidden');
    contentPanel.classList.remove('hidden');
    
    // Set basic headers
    document.getElementById('inspect-obj-name').textContent = name;
    document.getElementById('inspect-obj-id').textContent = id;
    
    const componentsList = document.getElementById('components-list');
    componentsList.innerHTML = '<div class="loading">Loading components...</div>';
    
    try {
        const objInfo = await callTool('inspect_object', { instance_id: id });
        
        document.getElementById('inspect-obj-type').textContent = objInfo.type || 'UnityEngine.GameObject';
        
        const activeContainer = document.getElementById('inspect-obj-active').parentNode;
        
        componentsList.innerHTML = '';
        
        if (objInfo.components !== null && objInfo.components !== undefined) {
            // It is a GameObject
            activeContainer.style.display = '';
            document.getElementById('inspect-obj-active').checked = objInfo.active_self !== false;
            
            if (objInfo.components.length > 0) {
                objInfo.components.forEach(comp => {
                    renderComponentCard(comp, componentsList);
                });
            } else {
                componentsList.innerHTML = '<div class="empty-state">No components attached to this GameObject.</div>';
            }
        } else {
            // It is a direct Component, ScriptableObject, or custom object
            activeContainer.style.display = 'none';
            
            const detailsContainer = document.createElement('div');
            detailsContainer.className = 'component-card-body expanded';
            detailsContainer.style.borderTop = 'none';
            componentsList.appendChild(detailsContainer);
            
            renderComponentDetails(objInfo, detailsContainer);
        }
    } catch (err) {
        componentsList.innerHTML = `<div class="empty-state" style="color:var(--accent-danger)">Error: ${err.message}</div>`;
    }
}

// Select Class & Load its Static Inspector panel
async function selectObjectClass(className) {
    state.activeObjectId = null;
    state.activeObjectName = className;
    state.expandedComponents.clear();
    state.componentsDetails = {};
    
    const emptyPanel = document.getElementById('inspector-empty');
    const contentPanel = document.getElementById('inspector-content');
    
    emptyPanel.classList.add('hidden');
    contentPanel.classList.remove('hidden');
    
    document.getElementById('inspect-obj-name').textContent = getShortName(className);
    document.getElementById('inspect-obj-id').textContent = 'Static';
    document.getElementById('inspect-obj-type').textContent = className;
    
    const activeContainer = document.getElementById('inspect-obj-active').parentNode;
    activeContainer.style.display = 'none';
    
    const componentsList = document.getElementById('components-list');
    componentsList.innerHTML = '<div class="loading">Loading static members...</div>';
    
    try {
        const objInfo = await callTool('inspect_object', { 
            class_name: className,
            include_methods: true
        });
        
        componentsList.innerHTML = '';
        
        const detailsContainer = document.createElement('div');
        detailsContainer.className = 'component-card-body expanded';
        detailsContainer.style.borderTop = 'none';
        componentsList.appendChild(detailsContainer);
        
        renderComponentDetails(objInfo, detailsContainer, className);
    } catch (err) {
        componentsList.innerHTML = `<div class="empty-state" style="color:var(--accent-danger)">Error: ${err.message}</div>`;
    }
}

// Render Component card accordion
function renderComponentCard(comp, parentContainer) {
    const card = document.createElement('div');
    card.className = 'component-card';
    
    const header = document.createElement('div');
    header.className = 'component-card-header';
    
    const title = document.createElement('div');
    title.className = 'component-card-title';
    
    // Shorten type namespace for simple display
    const typeName = getShortName(comp.type);
    title.innerHTML = `<strong>${typeName}</strong> <span style="font-size:11px;color:var(--text-muted)">(${comp.type})</span>`;
    
    const actions = document.createElement('div');
    actions.className = 'component-card-actions';
    
    const arrow = document.createElement('span');
    arrow.className = 'component-arrow';
    arrow.textContent = '▼';
    
    actions.appendChild(arrow);
    header.appendChild(title);
    header.appendChild(actions);
    card.appendChild(header);
    
    const body = document.createElement('div');
    body.className = 'component-card-body';
    card.appendChild(body);
    
    parentContainer.appendChild(card);
    
    // Accordion click
    header.addEventListener('click', async () => {
        if (state.expandedComponents.has(comp.instance_id)) {
            // Collapse
            state.expandedComponents.delete(comp.instance_id);
            arrow.classList.remove('expanded');
            body.classList.remove('expanded');
        } else {
            // Expand
            state.expandedComponents.add(comp.instance_id);
            arrow.classList.add('expanded');
            body.classList.add('expanded');
            
            if (!state.componentsDetails[comp.instance_id]) {
                body.innerHTML = '<div class="loading">Loading component properties...</div>';
                try {
                    const details = await callTool('inspect_object', { 
                        instance_id: comp.instance_id,
                        include_methods: true
                    });
                    state.componentsDetails[comp.instance_id] = details;
                    renderComponentDetails(details, body);
                } catch (err) {
                    body.innerHTML = `<div style="color:var(--accent-danger)">Failed to load: ${err.message}</div>`;
                }
            }
        }
    });
}

// Render component fields, properties, and methods
function renderComponentDetails(details, bodyContainer, className = null) {
    bodyContainer.innerHTML = '';
    
    // 0. Dedicated Material Properties View
    if (details.material_properties && !details.material_properties.error) {
        renderMaterialSection(details, bodyContainer);
    }
    
    // 1. Fields
    if (details.fields && details.fields.length > 0) {
        const fieldsSection = document.createElement('div');
        fieldsSection.className = 'inspect-table-container';
        fieldsSection.innerHTML = '<h4>Fields</h4>';
        
        const table = document.createElement('table');
        table.className = 'inspect-table';
        
        details.fields.forEach(f => {
            const tr = document.createElement('tr');
            
            const tdName = document.createElement('td');
            tdName.className = 'col-name';
            tdName.textContent = f.name;
            
            const tdValue = document.createElement('td');
            tdValue.className = 'col-value';
            
            const tdActions = document.createElement('td');
            tdActions.className = 'col-actions';
            
            renderValueEditor(details.instance_id, f.name, f.type, f.value, tdValue, tdActions, className);
            
            tr.appendChild(tdName);
            tr.appendChild(tdValue);
            tr.appendChild(tdActions);
            table.appendChild(tr);
        });
        
        fieldsSection.appendChild(table);
        bodyContainer.appendChild(fieldsSection);
    }
    
    // 2. Properties
    if (details.properties && details.properties.length > 0) {
        const propsSection = document.createElement('div');
        propsSection.className = 'inspect-table-container';
        propsSection.innerHTML = '<h4>Properties</h4>';
        
        const table = document.createElement('table');
        table.className = 'inspect-table';
        
        details.properties.forEach(p => {
            const tr = document.createElement('tr');
            
            const tdName = document.createElement('td');
            tdName.className = 'col-name';
            tdName.textContent = p.name;
            
            const tdValue = document.createElement('td');
            tdValue.className = 'col-value';
            
            const tdActions = document.createElement('td');
            tdActions.className = 'col-actions';
            
            renderValueEditor(details.instance_id, p.name, p.type, p.value, tdValue, tdActions, className);
            
            tr.appendChild(tdName);
            tr.appendChild(tdValue);
            tr.appendChild(tdActions);
            table.appendChild(tr);
        });
        
        propsSection.appendChild(table);
        bodyContainer.appendChild(propsSection);
    }
    
    // 3. Methods
    if (details.methods && details.methods.length > 0) {
        const methodsSection = document.createElement('div');
        methodsSection.className = 'inspect-table-container';
        methodsSection.innerHTML = '<h4>Methods</h4>';
        
        details.methods.forEach(m => {
            const item = document.createElement('div');
            item.className = 'method-item';
            
            const sig = document.createElement('div');
            sig.className = 'method-sig';
            
            const paramsSig = m.parameters ? m.parameters.map(p => `${getShortName(getParamType(p))} ${getParamName(p)}`).join(', ') : '';
            sig.textContent = `${getShortName(m.return_type)} ${m.name}(${paramsSig})`;
            
            const btnCall = document.createElement('button');
            btnCall.className = 'btn btn-secondary btn-sm';
            btnCall.textContent = 'Call';
            
            btnCall.addEventListener('click', () => {
                openInvokeModal(details.instance_id, m, className);
            });
            
            item.appendChild(sig);
            item.appendChild(btnCall);
            methodsSection.appendChild(item);
        });
        
        bodyContainer.appendChild(methodsSection);
    }
}

// Render dedicated Material Section (Shader metadata, keywords, and uniform properties)
function renderMaterialSection(details, bodyContainer) {
    const matProps = details.material_properties;
    if (!matProps) return;

    const section = document.createElement('div');
    section.className = 'inspect-table-container material-panel';

    const title = document.createElement('h4');
    title.textContent = 'Material & Shader Controls';
    section.appendChild(title);

    // Header: Shader, Render Queue, Keywords
    const headerCard = document.createElement('div');
    headerCard.className = 'material-header-card';

    // Shader Row
    const shaderRow = document.createElement('div');
    shaderRow.className = 'material-meta-row';
    const shaderLabel = document.createElement('span');
    shaderLabel.className = 'material-meta-label';
    shaderLabel.textContent = 'Active Shader';
    const shaderVal = document.createElement('div');
    shaderVal.className = 'material-meta-value';
    if (matProps.shader_name) {
        if (matProps.shader_instance_id) {
            const shaderLink = document.createElement('a');
            shaderLink.className = 'obj-ref-link';
            shaderLink.textContent = matProps.shader_name;
            shaderLink.title = `Inspect shader ${matProps.shader_instance_id}`;
            shaderLink.addEventListener('click', (e) => {
                e.preventDefault();
                selectObject(matProps.shader_instance_id, matProps.shader_name);
            });
            shaderVal.appendChild(shaderLink);
        } else {
            shaderVal.textContent = matProps.shader_name;
        }
    } else {
        shaderVal.innerHTML = '<span style="color:var(--text-muted);font-style:italic">None</span>';
    }
    shaderRow.appendChild(shaderLabel);
    shaderRow.appendChild(shaderVal);
    headerCard.appendChild(shaderRow);

    // Render Queue Row
    const queueRow = document.createElement('div');
    queueRow.className = 'material-meta-row';
    const queueLabel = document.createElement('span');
    queueLabel.className = 'material-meta-label';
    queueLabel.textContent = 'Render Queue';
    const queueVal = document.createElement('div');
    queueVal.className = 'material-meta-value';
    
    const queueInput = document.createElement('input');
    queueInput.className = 'inline-edit-input';
    queueInput.type = 'number';
    queueInput.style.width = '85px';
    queueInput.value = matProps.render_queue !== undefined ? matProps.render_queue : 2000;

    const queueSaveBtn = document.createElement('button');
    queueSaveBtn.className = 'btn-icon';
    queueSaveBtn.title = 'Save Render Queue';
    queueSaveBtn.innerHTML = '<img src="assets/save.svg" alt="Save" width="14" height="14">';
    queueSaveBtn.addEventListener('click', async () => {
        queueSaveBtn.disabled = true;
        try {
            await callTool('write_field', {
                instance_id: details.instance_id,
                name: 'renderQueue',
                value: queueInput.value
            });
            queueInput.style.borderColor = 'var(--accent-secondary)';
            setTimeout(() => { queueInput.style.borderColor = ''; }, 1000);
        } catch (err) {
            alert(`Failed to set render queue: ${err.message}`);
            queueInput.style.borderColor = 'var(--accent-danger)';
        } finally {
            queueSaveBtn.disabled = false;
        }
    });

    queueVal.appendChild(queueInput);
    queueVal.appendChild(queueSaveBtn);
    queueRow.appendChild(queueLabel);
    queueRow.appendChild(queueVal);
    headerCard.appendChild(queueRow);

    // Keywords Row
    const kwRow = document.createElement('div');
    kwRow.className = 'material-keywords-container';
    const kwLabel = document.createElement('span');
    kwLabel.className = 'material-meta-label';
    kwLabel.textContent = 'Shader Keywords';
    kwRow.appendChild(kwLabel);

    const chipsContainer = document.createElement('div');
    chipsContainer.className = 'keyword-chips';

    const keywords = Array.isArray(matProps.shader_keywords) ? matProps.shader_keywords : [];
    if (keywords.length === 0) {
        chipsContainer.innerHTML = '<span style="color:var(--text-muted);font-size:11px;font-style:italic">No active keywords</span>';
    } else {
        keywords.forEach(kw => {
            const chip = document.createElement('span');
            chip.className = 'keyword-chip';
            chip.textContent = kw;

            const removeBtn = document.createElement('span');
            removeBtn.className = 'keyword-remove-btn';
            removeBtn.textContent = '×';
            removeBtn.title = `Disable ${kw}`;
            removeBtn.addEventListener('click', async () => {
                try {
                    await callTool('invoke_method', {
                        instance_id: details.instance_id,
                        name: 'DisableKeyword',
                        args: [kw]
                    });
                    chip.remove();
                } catch (err) {
                    alert(`Failed to disable keyword: ${err.message}`);
                }
            });

            chip.appendChild(removeBtn);
            chipsContainer.appendChild(chip);
        });
    }

    const addKwContainer = document.createElement('div');
    addKwContainer.style.display = 'flex';
    addKwContainer.style.gap = '6px';
    addKwContainer.style.marginTop = '4px';

    const addKwInput = document.createElement('input');
    addKwInput.className = 'inline-edit-input';
    addKwInput.placeholder = 'Add keyword (e.g. _EMISSION)';
    addKwInput.style.flex = '1';

    const addKwBtn = document.createElement('button');
    addKwBtn.className = 'btn btn-secondary btn-sm';
    addKwBtn.textContent = 'Enable';
    addKwBtn.addEventListener('click', async () => {
        const kwName = addKwInput.value.trim();
        if (!kwName) return;
        addKwBtn.disabled = true;
        try {
            await callTool('invoke_method', {
                instance_id: details.instance_id,
                name: 'EnableKeyword',
                args: [kwName]
            });
            const updated = await callTool('inspect_object', { instance_id: details.instance_id, include_methods: true });
            renderComponentDetails(updated, bodyContainer);
        } catch (err) {
            alert(`Failed to enable keyword: ${err.message}`);
        } finally {
            addKwBtn.disabled = false;
        }
    });

    addKwContainer.appendChild(addKwInput);
    addKwContainer.appendChild(addKwBtn);
    kwRow.appendChild(chipsContainer);
    kwRow.appendChild(addKwContainer);
    headerCard.appendChild(kwRow);

    section.appendChild(headerCard);

    // Shader Properties Table
    if (matProps.properties && matProps.properties.length > 0) {
        const propTitle = document.createElement('h4');
        propTitle.style.marginTop = '12px';
        propTitle.textContent = 'Shader Properties';
        section.appendChild(propTitle);

        const table = document.createElement('table');
        table.className = 'inspect-table';

        matProps.properties.forEach(p => {
            const tr = document.createElement('tr');

            const tdName = document.createElement('td');
            tdName.className = 'col-name';
            tdName.innerHTML = `<div><strong>${p.name}</strong><span class="material-prop-type-badge">${p.type}</span></div>` +
                (p.description ? `<div style="font-size:11px;color:var(--text-muted)">${p.description}</div>` : '');

            const tdValue = document.createElement('td');
            tdValue.className = 'col-value';

            const tdActions = document.createElement('td');
            tdActions.className = 'col-actions';

            renderMaterialPropertyEditor(details.instance_id, p, tdValue, tdActions, bodyContainer);

            tr.appendChild(tdName);
            tr.appendChild(tdValue);
            tr.appendChild(tdActions);
            table.appendChild(tr);
        });

        section.appendChild(table);
    }

    bodyContainer.appendChild(section);
}

// Render dynamic Material property editor for shader properties
function renderMaterialPropertyEditor(instanceId, prop, valContainer, actContainer, bodyContainer) {
    const pType = prop.type;
    const val = prop.value;

    if (pType === 'Color') {
        const container = document.createElement('div');
        container.className = 'material-color-container';

        const colorInput = document.createElement('input');
        colorInput.type = 'color';
        colorInput.className = 'material-color-swatch';

        let hexVal = (val && val.hex) ? val.hex : '#ffffff';
        if (hexVal.startsWith('#') && hexVal.length > 7) {
            colorInput.value = hexVal.substring(0, 7);
        } else if (hexVal.startsWith('#')) {
            colorInput.value = hexVal;
        } else {
            colorInput.value = '#ffffff';
        }

        const hexTextInput = document.createElement('input');
        hexTextInput.className = 'inline-edit-input';
        hexTextInput.style.width = '90px';
        hexTextInput.value = hexVal;

        colorInput.addEventListener('input', () => {
            hexTextInput.value = colorInput.value;
        });

        container.appendChild(colorInput);
        container.appendChild(hexTextInput);
        valContainer.appendChild(container);

        const saveBtn = document.createElement('button');
        saveBtn.className = 'btn-icon';
        saveBtn.title = 'Save Color';
        saveBtn.innerHTML = '<img src="assets/save.svg" alt="Save" width="14" height="14">';

        const saveColor = async () => {
            saveBtn.disabled = true;
            try {
                await callTool('invoke_method', {
                    instance_id: instanceId,
                    name: 'SetColor',
                    args: [prop.name, hexTextInput.value]
                });
                hexTextInput.style.borderColor = 'var(--accent-secondary)';
                setTimeout(() => { hexTextInput.style.borderColor = ''; }, 1000);
            } catch (err) {
                alert(`Failed to set color: ${err.message}`);
                hexTextInput.style.borderColor = 'var(--accent-danger)';
            } finally {
                saveBtn.disabled = false;
            }
        };

        saveBtn.addEventListener('click', saveColor);
        hexTextInput.addEventListener('keypress', (e) => { if (e.key === 'Enter') saveColor(); });
        actContainer.appendChild(saveBtn);
        return;
    }

    if (pType === 'Range' || pType === 'Float') {
        const container = document.createElement('div');
        container.className = 'material-range-container';

        const hasRange = prop.range && prop.range.min !== undefined && prop.range.max !== undefined && prop.range.min < prop.range.max;
        const currentVal = typeof val === 'number' ? val : parseFloat(val) || 0;

        const numInput = document.createElement('input');
        numInput.type = 'number';
        numInput.step = 'any';
        numInput.className = 'inline-edit-input material-range-number';
        numInput.value = currentVal;

        let slider = null;
        if (hasRange) {
            slider = document.createElement('input');
            slider.type = 'range';
            slider.className = 'material-range-slider';
            slider.min = prop.range.min;
            slider.max = prop.range.max;
            slider.step = ((prop.range.max - prop.range.min) / 100).toString();
            slider.value = currentVal;

            slider.addEventListener('input', () => {
                numInput.value = slider.value;
            });
            numInput.addEventListener('input', () => {
                slider.value = numInput.value;
            });
            container.appendChild(slider);
        }

        container.appendChild(numInput);
        valContainer.appendChild(container);

        const saveBtn = document.createElement('button');
        saveBtn.className = 'btn-icon';
        saveBtn.title = 'Save Value';
        saveBtn.innerHTML = '<img src="assets/save.svg" alt="Save" width="14" height="14">';

        const saveFloat = async () => {
            saveBtn.disabled = true;
            try {
                await callTool('invoke_method', {
                    instance_id: instanceId,
                    name: 'SetFloat',
                    args: [prop.name, numInput.value]
                });
                numInput.style.borderColor = 'var(--accent-secondary)';
                setTimeout(() => { numInput.style.borderColor = ''; }, 1000);
            } catch (err) {
                alert(`Failed to set property ${prop.name}: ${err.message}`);
                numInput.style.borderColor = 'var(--accent-danger)';
            } finally {
                saveBtn.disabled = false;
            }
        };

        if (slider) {
            slider.addEventListener('change', saveFloat);
        }
        saveBtn.addEventListener('click', saveFloat);
        numInput.addEventListener('keypress', (e) => { if (e.key === 'Enter') saveFloat(); });
        actContainer.appendChild(saveBtn);
        return;
    }

    if (pType === 'Vector') {
        const container = document.createElement('div');
        container.className = 'material-vector-container';

        const vec = (val && typeof val === 'object') ? val : { x: 0, y: 0, z: 0, w: 0 };
        const inputs = ['x', 'y', 'z', 'w'].map(k => {
            const inp = document.createElement('input');
            inp.type = 'number';
            inp.step = 'any';
            inp.className = 'inline-edit-input';
            inp.placeholder = k.toUpperCase();
            inp.value = vec[k] !== undefined ? vec[k] : 0;
            container.appendChild(inp);
            return inp;
        });

        valContainer.appendChild(container);

        const saveBtn = document.createElement('button');
        saveBtn.className = 'btn-icon';
        saveBtn.title = 'Save Vector';
        saveBtn.innerHTML = '<img src="assets/save.svg" alt="Save" width="14" height="14">';

        const saveVector = async () => {
            saveBtn.disabled = true;
            try {
                const vecStr = inputs.map(i => i.value || '0').join(', ');
                await callTool('invoke_method', {
                    instance_id: instanceId,
                    name: 'SetVector',
                    args: [prop.name, vecStr]
                });
                inputs.forEach(i => {
                    i.style.borderColor = 'var(--accent-secondary)';
                    setTimeout(() => { i.style.borderColor = ''; }, 1000);
                });
            } catch (err) {
                alert(`Failed to set vector: ${err.message}`);
                inputs.forEach(i => { i.style.borderColor = 'var(--accent-danger)'; });
            } finally {
                saveBtn.disabled = false;
            }
        };

        saveBtn.addEventListener('click', saveVector);
        actContainer.appendChild(saveBtn);
        return;
    }

    if (pType === 'Texture') {
        if (val && typeof val === 'object' && val.instance_id !== undefined) {
            const link = document.createElement('a');
            link.className = 'obj-ref-link';
            link.textContent = `${val.name || 'Texture'} (${getShortName(val.type)})`;
            link.title = `Inspect texture ${val.instance_id}`;
            link.addEventListener('click', (e) => {
                e.preventDefault();
                selectObject(val.instance_id, val.name || 'Texture');
            });
            valContainer.appendChild(link);
        } else {
            valContainer.innerHTML = '<span style="color:var(--text-muted);font-style:italic">None (Texture)</span>';
        }

        const assignContainer = document.createElement('div');
        assignContainer.style.display = 'flex';
        assignContainer.style.gap = '4px';
        assignContainer.style.marginTop = '4px';

        const texIdInput = document.createElement('input');
        texIdInput.className = 'inline-edit-input';
        texIdInput.placeholder = 'Instance ID';
        texIdInput.style.width = '75px';

        const assignBtn = document.createElement('button');
        assignBtn.className = 'btn btn-secondary btn-sm';
        assignBtn.textContent = 'Set';
        assignBtn.addEventListener('click', async () => {
            const idVal = texIdInput.value.trim();
            if (!idVal) return;
            assignBtn.disabled = true;
            try {
                await callTool('invoke_method', {
                    instance_id: instanceId,
                    name: 'SetTexture',
                    args: [prop.name, idVal]
                });
                texIdInput.value = '';
                const updated = await callTool('inspect_object', { instance_id: instanceId, include_methods: true });
                renderComponentDetails(updated, bodyContainer);
            } catch (err) {
                alert(`Failed to set texture: ${err.message}`);
            } finally {
                assignBtn.disabled = false;
            }
        });

        assignContainer.appendChild(texIdInput);
        assignContainer.appendChild(assignBtn);
        valContainer.appendChild(assignContainer);
        return;
    }

    valContainer.textContent = val !== null && val !== undefined ? String(val) : '';
}

// Render dynamic fields/properties editor with type-appropriate inputs
function renderValueEditor(instanceId, name, type, value, valContainer, actContainer, className = null) {
    // 1. If value is null
    if (value === null) {
        valContainer.innerHTML = '<span style="color:var(--text-muted);font-style:italic">null</span>';
        return;
    }
    
    // 2. If reference type (Unity Object or cached object)
    if (typeof value === 'object' && value.instance_id !== undefined) {
        const link = document.createElement('a');
        link.className = 'obj-ref-link';
        link.textContent = `${value.name || 'Object'} (${getShortName(value.type)})`;
        link.title = `Inspect reference ${value.instance_id}`;
        link.addEventListener('click', (e) => {
            e.preventDefault();
            selectObject(value.instance_id, value.name || 'Reference Object');
        });
        valContainer.appendChild(link);
        return;
    }
    
    // 3. If collection/list
    if (Array.isArray(value)) {
        if (value.length === 0) {
            valContainer.innerHTML = '<span style="color:var(--text-muted);font-style:italic">Empty Array</span>';
            return;
        }

        const isObjRefList = value.some(item => item && typeof item === 'object' && item.instance_id !== undefined);
        if (isObjRefList) {
            const listContainer = document.createElement('div');
            listContainer.className = 'obj-ref-list';
            value.forEach((item, idx) => {
                if (item && typeof item === 'object' && item.instance_id !== undefined) {
                    const link = document.createElement('a');
                    link.className = 'obj-ref-link';
                    link.textContent = `[${idx}] ${item.name || 'Object'} (${getShortName(item.type)})`;
                    link.title = `Inspect reference ${item.instance_id}`;
                    link.addEventListener('click', (e) => {
                        e.preventDefault();
                        selectObject(item.instance_id, item.name || 'Reference Object');
                    });
                    listContainer.appendChild(link);
                } else {
                    const span = document.createElement('span');
                    span.style.color = 'var(--text-muted)';
                    span.style.fontSize = '12px';
                    span.textContent = `[${idx}] ${JSON.stringify(item)}`;
                    listContainer.appendChild(span);
                }
            });
            valContainer.appendChild(listContainer);
            return;
        }

        valContainer.innerHTML = `<span style="color:var(--text-secondary)">Array [${value.length}]</span>`;
        return;
    }
    
    // 4. Primitive / Editable values
    const isBool = type === 'System.Boolean';
    const isNumber = type === 'System.Int32' || type === 'System.Single' || type === 'System.Double' || type === 'System.Int16';
    
    if (isBool) {
        const label = document.createElement('label');
        label.className = 'switch-container';
        
        const input = document.createElement('input');
        input.type = 'checkbox';
        input.checked = value === true || value === 'True';
        
        const slider = document.createElement('span');
        slider.className = 'switch-slider';
        
        label.appendChild(input);
        label.appendChild(slider);
        valContainer.appendChild(label);
        
        input.addEventListener('change', async () => {
            try {
                const payload = className
                    ? { class_name: className, name: name, value: input.checked ? 'true' : 'false' }
                    : { instance_id: instanceId, name: name, value: input.checked ? 'true' : 'false' };
                await callTool('write_field', payload);
            } catch (err) {
                alert(`Failed to save field ${name}: ${err.message}`);
                input.checked = !input.checked;
            }
        });
    } else {
        // Text / Number input editor
        const input = document.createElement('input');
        input.className = 'inline-edit-input';
        input.type = 'text';
        input.value = value;
        valContainer.appendChild(input);
        
        const saveBtn = document.createElement('button');
        saveBtn.className = 'btn-icon';
        saveBtn.title = 'Save Changes';
        saveBtn.innerHTML = '<img src="assets/save.svg" alt="Save" width="14" height="14">';
        
        actContainer.appendChild(saveBtn);
        
        const saveHandler = async () => {
            saveBtn.disabled = true;
            try {
                const payload = className
                    ? { class_name: className, name: name, value: input.value }
                    : { instance_id: instanceId, name: name, value: input.value };
                await callTool('write_field', payload);
                input.style.borderColor = 'var(--accent-secondary)';
                setTimeout(() => { input.style.borderColor = ''; }, 1000);
            } catch (err) {
                alert(`Failed to save field ${name}: ${err.message}`);
                input.style.borderColor = 'var(--accent-danger)';
            } finally {
                saveBtn.disabled = false;
            }
        };
        
        saveBtn.addEventListener('click', saveHandler);
        input.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') saveHandler();
        });
    }
}

// Method Invoke Modal Logic
function openInvokeModal(instanceId, method, className = null) {
    const modal = document.getElementById('modal-invoke');
    const title = document.getElementById('modal-title');
    const sig = document.getElementById('modal-method-sig');
    const paramsContainer = document.getElementById('modal-params-fields');
    const resultBox = document.getElementById('modal-invoke-result');
    const invokeBtn = document.getElementById('btn-modal-invoke');
    
    title.textContent = `Invoke ${method.name}`;
    
    const paramsSig = method.parameters ? method.parameters.map(p => `${getShortName(getParamType(p))} ${getParamName(p)}`).join(', ') : '';
    sig.textContent = `${getShortName(method.return_type)} ${method.name}(${paramsSig})`;
    
    paramsContainer.innerHTML = '';
    resultBox.classList.add('hidden');
    
    const inputFields = [];
    
    if (method.parameters && method.parameters.length > 0) {
        method.parameters.forEach(p => {
            const group = document.createElement('div');
            group.className = 'param-input-group';
            
            const label = document.createElement('label');
            label.textContent = `${getParamName(p)} (${getShortName(getParamType(p))}):`;
            
            const input = document.createElement('input');
            input.type = 'text';
            input.className = 'form-control';
            input.placeholder = `Value for ${p.Name}`;
            
            group.appendChild(label);
            group.appendChild(input);
            paramsContainer.appendChild(group);
            
            inputFields.push(input);
        });
    } else {
        paramsContainer.innerHTML = '<div style="color:var(--text-muted);font-style:italic">This method takes no parameters.</div>';
    }
    
    modal.classList.remove('hidden');
    
    // Clear and set Invoke button listener
    const newInvokeBtn = invokeBtn.cloneNode(true);
    invokeBtn.parentNode.replaceChild(newInvokeBtn, invokeBtn);
    
    newInvokeBtn.addEventListener('click', async () => {
        newInvokeBtn.disabled = true;
        newInvokeBtn.textContent = 'Invoking...';
        
        const args = inputFields.map(f => f.value);
        
        try {
            const payload = className
                ? { class_name: className, name: method.name, args: args }
                : { instance_id: instanceId, name: method.name, args: args };
            const invokeResult = await callTool('invoke_method', payload);
            
            resultBox.classList.remove('hidden');
            const resultText = document.getElementById('modal-result-text');
            
            if (invokeResult === null) {
                resultText.textContent = 'void/null';
            } else if (typeof invokeResult === 'object') {
                resultText.textContent = JSON.stringify(invokeResult, null, 2);
            } else {
                resultText.textContent = String(invokeResult);
            }
        } catch (err) {
            resultBox.classList.remove('hidden');
            document.getElementById('modal-result-text').textContent = `Error: ${err.message}`;
        } finally {
            newInvokeBtn.disabled = false;
            newInvokeBtn.textContent = 'Invoke';
        }
    });
}

function closeModal() {
    document.getElementById('modal-invoke').classList.add('hidden');
}

// Search Action
async function executeSearch() {
    const input = document.getElementById('input-search-class');
    const resultsContainer = document.getElementById('search-results');
    const query = input.value.trim();
    
    if (!query) return;
    
    resultsContainer.innerHTML = '<div class="loading">Searching...</div>';
    
    const mode = document.querySelector('input[name="search-mode"]:checked').value;
    
    try {
        if (mode === 'instances') {
            const includeAssets = document.getElementById('chk-search-include-assets').checked;
            const results = await callTool('find_objects', { class_name: query, include_assets: includeAssets });
            resultsContainer.innerHTML = '';
            
            if (results && results.length > 0) {
                results.forEach(res => {
                    const card = document.createElement('div');
                    card.className = 'search-result-item';
                    
                    const name = document.createElement('div');
                    name.className = 'result-name';
                    name.textContent = res.name || 'Unnamed Object';
                    
                    const type = document.createElement('div');
                    type.className = 'result-type';
                    type.textContent = `ID: ${res.instance_id} | ${res.type}`;
                    
                    card.appendChild(name);
                    card.appendChild(type);
                    resultsContainer.appendChild(card);
                    
                    card.addEventListener('click', () => {
                        selectObject(res.instance_id, res.name || 'GameObject');
                    });
                });
            } else {
                resultsContainer.innerHTML = '<div class="empty-state">No matching objects found. Make sure the type name is fully qualified (e.g. UnityEngine.Camera).</div>';
            }
        } else {
            // Classes search mode
            const results = await callTool('find_types', { query: query });
            resultsContainer.innerHTML = '';
            
            if (results && results.length > 0) {
                results.forEach(typeName => {
                    const card = document.createElement('div');
                    card.className = 'search-result-item';
                    
                    const name = document.createElement('div');
                    name.className = 'result-name';
                    const shortName = getShortName(typeName);
                    name.textContent = shortName;
                    
                    const type = document.createElement('div');
                    type.className = 'result-type';
                    type.textContent = typeName;
                    
                    const btnRow = document.createElement('div');
                    btnRow.style.display = 'flex';
                    btnRow.style.gap = '8px';
                    btnRow.style.marginTop = '6px';
                    
                    const btnFind = document.createElement('button');
                    btnFind.className = 'btn btn-primary btn-sm';
                    btnFind.textContent = 'Find Instances';
                    btnFind.addEventListener('click', (e) => {
                        e.stopPropagation();
                        document.querySelector('input[name="search-mode"][value="instances"]').checked = true;
                        document.getElementById('search-option-assets-container').classList.remove('hidden');
                        input.value = typeName;
                        executeSearch();
                    });
                    
                    const btnStatics = document.createElement('button');
                    btnStatics.className = 'btn btn-secondary btn-sm';
                    btnStatics.textContent = 'Inspect Statics';
                    btnStatics.addEventListener('click', (e) => {
                        e.stopPropagation();
                        selectObjectClass(typeName);
                    });
                    
                    btnRow.appendChild(btnFind);
                    btnRow.appendChild(btnStatics);
                    
                    card.appendChild(name);
                    card.appendChild(type);
                    card.appendChild(btnRow);
                    resultsContainer.appendChild(card);
                    
                    card.addEventListener('click', () => {
                        document.querySelector('input[name="search-mode"][value="instances"]').checked = true;
                        document.getElementById('search-option-assets-container').classList.remove('hidden');
                        input.value = typeName;
                        executeSearch();
                    });
                });
            } else {
                resultsContainer.innerHTML = '<div class="empty-state">No matching classes found. Try a simpler keyword (e.g. "Controller" or "Camera").</div>';
            }
        }
    } catch (err) {
        resultsContainer.innerHTML = `<div class="empty-state" style="color:var(--accent-danger)">Search failed: ${err.message}</div>`;
    }
}

// Live Screenshot Refresher
async function refreshScreenshot() {
    const placeholder = document.getElementById('screenshot-placeholder');
    const img = document.getElementById('gameview-img');
    const refreshBtn = document.getElementById('btn-refresh-screenshot');
    
    refreshBtn.disabled = true;
    
    try {
        const screenshotData = await callTool('take_screenshot', { scale: state.screenshotScale });
        
        if (screenshotData && screenshotData.base64) {
            img.src = `data:image/png;base64,${screenshotData.base64}`;
            img.classList.remove('hidden');
            placeholder.classList.add('hidden');
        } else {
            placeholder.textContent = 'Invalid screenshot format received.';
            placeholder.classList.remove('hidden');
            img.classList.add('hidden');
        }
    } catch (err) {
        console.error('Failed to capture screenshot:', err);
        placeholder.textContent = `Capture failed: ${err.message}`;
        placeholder.classList.remove('hidden');
        img.classList.add('hidden');
    } finally {
        refreshBtn.disabled = false;
    }
}

// Auto Refresh Screenshot Control
function toggleAutoRefresh(enabled, rate) {
    if (state.autoRefreshInterval) {
        clearInterval(state.autoRefreshInterval);
        state.autoRefreshInterval = null;
    }
    
    if (enabled) {
        state.autoRefreshInterval = setInterval(refreshScreenshot, rate);
    }
}
