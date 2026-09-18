# Material Inspection & Modification

This document details the architectural design and implementation for inspecting and modifying Unity `Material` instances and their shader uniform properties within UnityRuntimeMCP.

---

## 1. Background & Challenge

In Unity, `UnityEngine.Material` visual properties are defined by shader uniforms (e.g. `_Color`, `_BaseColor`, `_MainTex`, `_BumpMap`, `_Metallic`, `_Smoothness`, `_EmissionColor`, etc.). 

These uniform properties:
- Are stored in native C++ engine memory managed by the shader program.
- Are **not** managed C# fields or properties on `UnityEngine.Material`.
- Cannot be discovered through standard .NET reflection (`System.Type.GetFields()` / `GetProperties()`).
- Must be accessed via Unity's dedicated material methods (`GetColor`, `GetFloat`, `GetVector`, `GetTexture`, `SetColor`, `SetFloat`, etc.) and shader reflection methods (`GetPropertyCount`, `GetPropertyName`, `GetPropertyType`, `GetPropertyRangeLimits`).

---

## 2. Server-Side Implementation

### 2.1 Shader Uniform Extraction ([`InspectObjectTool.cs`](../src/Tools/InspectObjectTool.cs))

When `inspect_object` is invoked on an instance ID resolving to a `UnityEngine.Material`, `InspectObjectTool` enriches the response payload with a `material_properties` object:

```json
{
  "instance_id": 123456,
  "name": "Default-Material (Instance)",
  "type": "UnityEngine.Material",
  "material_properties": {
    "shader_name": "Standard",
    "shader_instance_id": 54321,
    "render_queue": 2000,
    "shader_keywords": ["_EMISSION", "_NORMALMAP"],
    "properties": [
      {
        "name": "_Color",
        "description": "Main Color",
        "type": "Color",
        "value": {
          "r": 1.0,
          "g": 1.0,
          "b": 1.0,
          "a": 1.0,
          "hex": "#FFFFFFFF"
        },
        "range": null
      },
      {
        "name": "_Metallic",
        "description": "Metallic",
        "type": "Range",
        "value": 0.5,
        "range": { "min": 0.0, "max": 1.0 }
      }
    ]
  },
  "fields": [],
  "properties": [ ... ],
  "methods": [ ... ]
}
```

#### Extraction Mechanism:
1. **Shader Query**: Obtains `mat.shader`.
2. **Property Count**: Queries `shader.GetPropertyCount()`.
3. **Property Metadata**: Iterates property indices to extract name (`GetPropertyName`), description (`GetPropertyDescription`), and property type (`GetPropertyType`).
4. **Range Limits**: For `Range` properties, dynamically inspects `shader.GetPropertyRangeLimits(index, 1|2)` to obtain min and max float limits.
5. **Runtime Value Extraction**:
   - `Color`: `mat.GetColor(propName)` converted to RGBA float values and HTML hex (`#RRGGBBAA`).
   - `Float` / `Range`: `mat.GetFloat(propName)`.
   - `Vector`: `mat.GetVector(propName)` decomposed into `{ x, y, z, w }`.
   - `Texture`: `mat.GetTexture(propName).ToMcpValue()`, registering the texture in the cache with its instance ID.
6. **Material State**: Captures `mat.renderQueue` and active `mat.shaderKeywords`.

---

### 2.2 Method Overload Disambiguation ([`InvokeMethodTool.cs`](../src/Tools/InvokeMethodTool.cs))

In Unity's `UnityEngine.Material`, all property setters and getters provide dual overloads:
- `SetColor(string name, Color value)` vs `SetColor(int nameID, Color value)`
- `SetFloat(string name, float value)` vs `SetFloat(int nameID, float value)`
- `SetVector(string name, Vector4 value)` vs `SetVector(int nameID, Vector4 value)`
- `SetTexture(string name, Texture value)` vs `SetTexture(int nameID, Texture value)`

Because reflection does not guarantee declaration order, matching solely on parameter count caused string property names (e.g. `"_Color"`) to be passed to integer `nameID` parameters, triggering `FormatException`.

`InvokeMethodTool` now validates argument compatibility: when multiple candidate overloads have identical parameter counts, it matches string arguments to string parameters unless the argument successfully parses as an integer.

---

### 2.3 Extended Type Conversion ([`InvokeMethodTool.cs`](../src/Tools/InvokeMethodTool.cs) & [`WriteFieldTool.cs`](../src/Tools/WriteFieldTool.cs))

`ConvertValue` was extended across both tools to support Unity-specific types from string representations:

| Target Type | Supported Formats | Example |
| :--- | :--- | :--- |
| `UnityEngine.Color` | HTML hex string (`#RGB`, `#RRGGBB`, `#RRGGBBAA`), comma/space delimited floats | `"#FF0000"`, `"1.0, 0.0, 0.0, 1.0"` |
| `UnityEngine.Vector4` | Comma/space delimited 4-element coordinates | `"0, 1, 0, 0"` |
| `UnityEngine.Vector2` | Comma/space delimited 2-element coordinates | `"1.0, 0.5"` |
| `UnityEngine.Object` | Instance ID integer (looked up via object cache) | `"1000000045"` |

---

## 3. Web UI Implementation

### 3.1 Object Reference Array Navigation ([`app.js`](../src/WebUI/app.js))

Previously, inspecting a `Renderer` (e.g. `MeshRenderer`, `SkinnedMeshRenderer`) displayed the `materials` and `sharedMaterials` array properties as unclickable text (`Array [2]`).

`renderValueEditor` now checks whether array items are serialized Unity object references (`item.instance_id`). If so, each element is rendered as an interactive reference link:

```text
materials: [0] Mat_Body (Material)
           [1] Mat_Glass (Material)
```

Clicking any item navigates directly to `selectObject(item.instance_id)`, opening the dedicated Material Inspector.

---

### 3.2 Dedicated Material Inspector ([`app.js`](../src/WebUI/app.js) & [`style.css`](../src/WebUI/style.css))

When an inspected object has `material_properties`, `renderComponentDetails` displays a dedicated **Material & Shader Controls** panel:

1. **Header & Metadata**:
   - **Active Shader**: Displays shader name with an interactive link to inspect the shader object directly.
   - **Render Queue**: Inline numeric input with a save button, routed to `write_field(instance_id, "renderQueue", value)`.
   - **Shader Keywords**: Active keywords rendered as removable chips. Clicking `×` calls `invoke_method(instance_id, "DisableKeyword", [keyword])`. An input field with an "Enable" button calls `invoke_method(instance_id, "EnableKeyword", [keyword])`.

2. **Shader Properties Table**:
   - **Color Properties**: Color swatch preview with native `<input type="color">` picker and hex input. Modifying saves via `invoke_method(instance_id, "SetColor", [propName, hexVal])`.
   - **Range & Float Properties**: For properties with defined range limits, renders an interactive range slider synchronized with a numeric input. Modifying saves via `invoke_method(instance_id, "SetFloat", [propName, floatVal])`.
   - **Vector Properties**: 4-component input grid (X, Y, Z, W) with save action via `invoke_method(instance_id, "SetVector", [propName, vecStr])`.
   - **Texture Slots**: Displays current texture reference link, plus an instance ID input field to assign a new texture via `invoke_method(instance_id, "SetTexture", [propName, textureId])`.
