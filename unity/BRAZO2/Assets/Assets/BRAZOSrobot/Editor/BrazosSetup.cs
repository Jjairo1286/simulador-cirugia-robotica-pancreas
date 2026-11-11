using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BrazosRobot.Editor
{
    /// <summary>
    /// Menú "BRAZOSrobot": configura el proyecto (puerto serie), crea la escena y lanza la autoprueba.
    /// Al abrir el proyecto por primera vez configura solo el Api Compatibility Level.
    /// </summary>
    [InitializeOnLoad]
    public static class BrazosSetup
    {
        const string ESCENA = "Assets/Assets/BRAZOSrobot/BRAZOSrobot.unity";

        static BrazosSetup()
        {
            EditorApplication.delayCall += () =>
            {
                // videos.flag con "no": grabación cancelada, solo se borra la bandera
                try
                {
                    if (System.IO.File.Exists(Demostracion.Bandera) && System.IO.File.ReadAllText(Demostracion.Bandera).Trim() == "no")
                        System.IO.File.Delete(Demostracion.Bandera);
                }
                catch (System.Exception) { }
                // autoprueba de los niveles: si existe autoprueba.flag junto a Assets, entra solo a Play
                if ((System.IO.File.Exists(AutoPrueba.Bandera) || System.IO.File.Exists(Demostracion.Bandera)) && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.Log("BRAZOSrobot: autoprueba de niveles (autoprueba.flag).");
                    EditorApplication.EnterPlaymode();
                    return;
                }
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (SessionState.GetBool("brazos-config", false)) return;
                SessionState.SetBool("brazos-config", true);
                Configurar();
                // la escena no se crea sola (reemplazaba la escena abierta): el simulador arranca en cualquier
                // escena al dar Play; para guardar una propia está el menú BRAZOSrobot > Crear escena
            };
        }

        [MenuItem("BRAZOSrobot/Configurar proyecto (puerto serie)")]
        public static void Configurar()
        {
            // System.IO.Ports (lectura de la ESP32) solo existe con el perfil .NET Framework
            if (PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone) != ApiCompatibilityLevel.NET_Unity_4_8)
                PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
            PlayerSettings.runInBackground = true;
            // el panel usa el teclado y el ratón "clásicos": si el proyecto solo tiene el Input System nuevo, activa ambos
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps != null && ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var p = so.FindProperty("activeInputHandler");
                if (p != null && p.intValue == 1)
                {
                    p.intValue = 2; so.ApplyModifiedProperties();
                    EditorUtility.DisplayDialog("BRAZOSrobot", "Se activó el teclado y ratón clásicos (Active Input Handling = Both). Reinicia Unity para aplicarlo.", "OK");
                }
            }
            Debug.Log("BRAZOSrobot: Api Compatibility Level = .NET Framework (necesario para leer la ESP32 por el puerto serie).");
        }

        [MenuItem("BRAZOSrobot/Autoprueba de niveles (resultado en Logs)")]
        public static void Autoprueba()
        {
            System.IO.File.WriteAllText(AutoPrueba.Bandera, "1");
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }

        [MenuItem("BRAZOSrobot/Grabar videos de demostración (carpeta Videos)")]
        public static void GrabarVideos()
        {
            System.IO.File.WriteAllText(Demostracion.Bandera, "");
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }

        [MenuItem("BRAZOSrobot/Crear escena")]
        public static void CrearEscena()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("BRAZOSrobot").AddComponent<BrazosApp>();
            EditorSceneManager.SaveScene(escena, ESCENA);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ESCENA, true) };
            Debug.Log("BRAZOSrobot: escena creada en " + ESCENA + ". Dale Play.");
        }
    }
}
