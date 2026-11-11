# PCB del mando bimanual

Placa de una cara, de 100 x 68.5 mm, diseñada en Proteus (ISIS para el esquemático y ARES para el ruteo). Concentra el cableado de los dos mandos: la ESP32 va en su propio zócalo, los dos TCA9548A también, y hay conectores para los 12 encoders AS5600 (R AS1 a R AS6 del lado derecho, L AS1 a L AS6 del izquierdo), los joysticks, la fuente externa de 3.3 V y la alimentación de los sensores.

## Archivos

`PCB_Planchado_Acetato.pdf` sirve para fabricar la placa por planchado. Tiene dos hojas, cada una con tres copias para repuestos:

1. Cara inferior (cobre), en orientación normal, tal como se ve en Proteus. Los círculos blancos marcan dónde perforar.
2. Cara superior (serigrafía), en espejo a propósito para que el texto se lea bien después de planchar.

Hay que imprimir al 100 %, sin "ajustar a página". La regla del pie de página debe medir 50 mm en el papel. Si mide otra cosa, la escala está mal y los pines no van a calzar.
