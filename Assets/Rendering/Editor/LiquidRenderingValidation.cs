using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Renders a disposable preview scene; never modifies the user's scene or materials.</summary>
public static class LiquidRenderingValidation
{
    const string Output = "Temp/LiquidRenderingValidation";

    [MenuItem("Tools/Rendering/Validate Liquid Rendering")]
    public static void Validate()
    {
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        var errors = new List<string>();
        Application.LogCallback capture = (message, trace, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message + "\n" + trace);
        };
        Application.logMessageReceived += capture;
        var materials = new List<Material>();
        Scene scene = default;
        RenderTexture target = null;
        var previousTarget = RenderTexture.active;
        try
        {
            foreach (string name in new[] { "Custom/ToonLiquid", "Hidden/OutlineShader", "Hidden/TransparentOutline", "Hidden/CompositeShader" })
            {
                var shader = Shader.Find(name);
                if (shader == null) throw new Exception("Missing shader " + name);
                var material = new Material(shader);
                materials.Add(material);
                for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                {
                    report.AppendLine(name + ": " + message.severity + " " + message.message);
                    if (message.severity.ToString() == "Error") errors.Add(message.message);
                }
                report.AppendLine(name + ": " + material.passCount + " passes; supported=" + shader.isSupported);
                if (!shader.isSupported) errors.Add(name + " is not supported");
            }

            scene = EditorSceneManager.NewPreviewScene();
            GameObject Make(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }
            var camera = Make("Liquid validation camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.transform.position = new Vector3(0, 1, -7);
            camera.transform.LookAt(new Vector3(0, 0.4f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.32f, 0.36f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30;
            camera.orthographicSize = 2.5f;
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            var light = Make("Liquid validation light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35, -35, 0);
            light.intensity = 1;
            var liquid = materials[0];
            liquid.SetFloat("_WaveStrength", 0.08f);
            var opaque = new Material(Shader.Find("Custom/ToonLit"));
            opaque.SetColor("_BaseColor", new Color(0.8f, 0.5f, 0.2f));
            materials.Add(opaque);
            GameObject Shape(PrimitiveType type, Vector3 position, Vector3 scale, Material material)
            {
                var go = GameObject.CreatePrimitive(type);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.position = position;
                go.transform.localScale = scale;
                go.GetComponent<Renderer>().sharedMaterial = material;
                ObjectIdBinding.Bind(go.GetComponent<Renderer>());
                return go;
            }
            var sphere = Shape(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 2.4f, liquid);
            Shape(PrimitiveType.Cube, new Vector3(0, 0, 1.7f), new Vector3(0.5f, 2.6f, 0.5f), opaque);
            Shape(PrimitiveType.Cube, new Vector3(1.15f, -0.2f, -1.4f), new Vector3(0.6f, 2, 0.4f), opaque);
            Shape(PrimitiveType.Sphere, new Vector3(-1.1f, 0.3f, 0.8f), Vector3.one * 1.5f, liquid);
            target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
            target.Create();
            void Capture(string file)
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                var image = new Texture2D(640, 360, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(Output, file), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                report.AppendLine("Rendered " + file);
            }
            foreach (bool ortho in new[] { false, true })
            foreach (float opacity in new[] { 0f, 0.5f, 1f })
            {
                camera.orthographic = ortho;
                liquid.SetColor("_BaseColor", new Color(0.15f, 0.65f, 0.8f, opacity));
                Capture((ortho ? "orthographic" : "perspective") + "-" + opacity.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png");
            }
            liquid.SetColor("_BaseColor", new Color(0.15f, 0.65f, 0.8f, 0.5f));
            liquid.SetFloat("_OutlineOpacity", 0);
            Capture("outline-disabled.png");
            liquid.SetFloat("_OutlineOpacity", 1);
            var properties = new MaterialPropertyBlock();
            var renderer = sphere.GetComponent<Renderer>();
            renderer.GetPropertyBlock(properties);
            properties.SetFloat("_Highlighted", 1);
            renderer.SetPropertyBlock(properties);
            Capture("highlighted.png");
        }
        catch (Exception exception) { errors.Add(exception.ToString()); }
        finally
        {
            RenderTexture.active = previousTarget;
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            foreach (var material in materials) UnityEngine.Object.DestroyImmediate(material);
            Application.logMessageReceived -= capture;
            report.AppendLine("Errors: " + errors.Count);
            foreach (var error in errors) report.AppendLine(error);
            File.WriteAllText(Path.Combine(Output, "report.txt"), report.ToString());
        }
    }
}
