# Simulador de cirugía robótica de páncreas

Prototipo para la simulación y entrenamiento de cirugía robótica de páncreas. Proyecto de grado de Ingeniería Biomédica, Universidad Católica Boliviana "San Pablo", La Paz.

Autor: Jairo Marcelo Canqui Montoya

El sistema tiene dos manipuladores maestros de 6 grados de libertad, inspirados en la consola de Hugo RAS. Cada uno lleva seis encoders magnéticos AS5600 y un joystick que funciona como gatillo de la pinza. Una ESP32 lee los dos mandos y manda los datos por el puerto serie a un simulador hecho en Unity, donde se practican ejercicios con ambas manos.

## Contenido

| Carpeta | Qué hay |
|---|---|
| `firmware/` | Código de la ESP32 que lee los encoders y los joysticks |
| `hardware/pcb/` | Placa del mando diseñada en Proteus y el arte para fabricarla por planchado |

El repositorio se va completando por partes.
