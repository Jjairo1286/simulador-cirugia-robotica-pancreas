using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BrazosRobot.Editor
{
    /// <summary>
    /// Menú "BRAZOSrobot/Exportar .exe (Windows)": deja el juego en la carpeta Build del proyecto.
    /// Como toda la escena se crea por código, antes de compilar guarda dos materiales en Resources
    /// para que el shader Standard (opaco y transparente) entre en el ejecutable; sin eso todo
    /// saldría de color magenta. El resultado queda en Logs/exportar.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class BrazosBuild
    {
        static string Raiz { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        static string Bandera { get { return Path.Combine(Raiz, "exportar.flag"); } }

        static BrazosBuild()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(Bandera) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                File.Delete(Bandera);
                Exportar();
            };
        }

        static void Materiales()
        {
            var guids = AssetDatabase.FindAssets("BrazosApp t:MonoScript");
            string carpeta = guids.Length > 0
                ? Path.GetDirectoryName(Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guids[0]))).Replace('\\', '/') + "/Resources"
                : "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(carpeta))
            {
                Directory.CreateDirectory(carpeta);
                AssetDatabase.Refresh();
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(carpeta + "/BrazosOpaco.mat") == null)
                AssetDatabase.CreateAsset(Geo.Mat(0xffffff), carpeta + "/BrazosOpaco.mat");
            if (AssetDatabase.LoadAssetAtPath<Material>(carpeta + "/BrazosTransparente.mat") == null)
                AssetDatabase.CreateAsset(Geo.Transparente(0xffffff, 0.4f), carpeta + "/BrazosTransparente.mat");
            AssetDatabase.SaveAssets();
        }

        [MenuItem("BRAZOSrobot/Exportar .exe (Windows)")]
        public static void Exportar()
        {
            var log = new System.Text.StringBuilder();
            string salida = Path.Combine(Raiz, "Build", "BRAZOSrobot.exe");
            try
            {
                BrazosSetup.Configurar();
                Materiales();
                // escena: la que está abierta; si no está guardada, la más reciente del proyecto
                string escena = EditorSceneManager.GetActiveScene().path;
                if (string.IsNullOrEmpty(escena))
                    escena = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                        .Where(p => !p.Contains("_Recovery")).OrderByDescending(p => File.GetLastWriteTimeUtc(p)).FirstOrDefault();
                if (string.IsNullOrEmpty(escena)) { BrazosSetup.CrearEscena(); escena = EditorSceneManager.GetActiveScene().path; }
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(escena, true) };
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                PlayerSettings.resizableWindow = true;
                log.AppendLine("Escena: " + escena);

                var op = new BuildPlayerOptions { scenes = new[] { escena }, locationPathName = salida, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None };
                BuildReport r = BuildPipeline.BuildPlayer(op);
                log.AppendLine("Resultado: " + r.summary.result + "  errores=" + r.summary.totalErrors + "  avisos=" + r.summary.totalWarnings
                    + "  tamaño=" + (r.summary.totalSize / 1048576f).ToString("0.0") + " MB  tiempo=" + r.summary.totalTime);
                log.AppendLine("Salida: " + salida);
                foreach (var paso in r.steps)
                    foreach (var m in paso.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception) log.AppendLine("ERROR [" + paso.name + "]: " + m.content);
                if (r.summary.result == BuildResult.Succeeded) EditorUtility.RevealInFinder(salida);
            }
            catch (System.Exception e) { log.AppendLine("EXCEPCIÓN: " + e); }
            Directory.CreateDirectory(Path.Combine(Raiz, "Logs"));
            File.WriteAllText(Path.Combine(Raiz, "Logs", "exportar.txt"), log.ToString());
            Debug.Log("BRAZOSrobot — exportar:\n" + log);
        }
    }
}
