# Simulador en Unity (BRAZO2)

Proyecto de Unity con los ejercicios de entrenamiento. Recibe por el puerto serie los 14 datos del firmware (6 ejes y gatillo por mano), mueve los dos instrumentos dentro de un modelo anatómico de la zona del páncreas y califica cada intento.

Los ejercicios todavía pueden cambiar. Esta carpeta se va a ir actualizando.

## Abrir el proyecto

1. Instalar Unity 6 (versión 6000.3.8f1, la que figura en `ProjectSettings/ProjectVersion.txt`) desde Unity Hub.
2. En Unity Hub, Add > Add project from disk y elegir la carpeta `BRAZO2`. La primera vez tarda porque Unity regenera la carpeta `Library`.
3. Ejecutar el menú BRAZOSrobot > Configurar proyecto (puerto serie). Deja listo el acceso a `System.IO.Ports`, que hace falta para leer la ESP32.
4. Ejecutar BRAZOSrobot > Crear escena. Arma la escena `BRAZOSrobot.unity` desde los scripts.
5. Conectar el mando, darle Play y elegir el puerto en el panel izquierdo.

Desde el mismo menú se puede correr la autoprueba de los niveles, grabar los videos de demostración y exportar un .exe para Windows.

## Ejercicios

El detalle de cada nivel, la percepción de profundidad, el puntaje y las simplificaciones del modelo están en [`BRAZO2/NIVELES_LEEME.txt`](BRAZO2/NIVELES_LEEME.txt). En resumen:

| Nivel | Ejercicio | Script |
|---|---|---|
| 1 | Anillos: transferencia de anillos entre postes | `Practica.cs`, `NivelTejido.cs` |
| 2 | Tejido: exposición y suspensión del páncreas | `NivelTejido.cs` |
| 3 | Bimanual: cinta por el túnel retropancreático | `NivelBimanual.cs` |
| 4 | Disección: exposición de la vena mesentérica superior | `NivelDiseccion.cs` |
| 5 | Vasos: clipado y corte de ramas de la vena esplénica | `NivelVasos.cs` |

Cada intento se guarda en `BRAZOSrobot_resultados.csv`, con el puntaje y las métricas.

El código de un sexto nivel de sutura (`NivelSutura.cs`) sigue en el proyecto, pero ese ejercicio ya no forma parte del simulador.

## Scripts principales

Están en `BRAZO2/Assets/Assets/BRAZOSrobot/Scripts/`.

| Script | Función |
|---|---|
| `EntradaSerie.cs` | Lee la ESP32 por el puerto serie en un hilo aparte y separa los 14 datos |
| `Mando.cs` | Geometría del mando maestro de 6 ejes y gatillo, con el izquierdo en espejo del derecho (los mismos ejes que el visor HTML) |
| `Instrumento.cs` | Instrumentos de muñeca de 8 mm tipo EndoWrist, con el largo y la apertura de las mordazas |
| `Anatomia.cs` | Modelo anatómico a escala real y contacto con los vasos |
| `Simulacion.cs` | Tejido deformable, cinta, sangrado, métricas, puntaje y panel |
| `Profundidad.cs` | Ayudas de profundidad: luz del endoscopio, sombras, indicador y anaglifo 3D |
| `BrazosApp.cs` | Programa principal: une mandos, instrumentos y niveles |
| `AutoPrueba.cs`, `Demostracion.cs` | Autoprueba de los niveles y grabación de videos |

## Qué no está en el repositorio

`Library`, `Temp`, `Logs`, `UserSettings` y `Build` se generan solos al abrir o compilar el proyecto. Las carpetas de respaldo tampoco están, porque el historial de git cumple esa función. Los videos de demostración de cada nivel (`Videos/`) pesan unos 130 MB y se dejaron fuera para que el repositorio no crezca tanto.
