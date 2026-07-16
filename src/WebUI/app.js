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
