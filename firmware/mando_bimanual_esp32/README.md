# Firmware de adquisición del mando bimanual (ESP32)

Lee los 12 encoders magnéticos AS5600 de los dos manipuladores (6 por mano) a través de dos multiplexores TCA9548A en el mismo bus I2C, junto con los dos joysticks que hacen de gatillo de la pinza. Envía todo por el puerto serie a unos 60 Hz.

## Formato de salida

Una línea por lectura, 115200 baudios, 14 valores separados por coma:

```
d1,d2,d3,d4,d5,d6,gatilloD,i1,i2,i3,i4,i5,i6,gatilloI
```

Los ejes van en grados, de 0.00 a 359.91, y valen -1 cuando el encoder no responde. El gatillo es el porcentaje presionado: 0 con el joystick suelto (pinza abierta, 60°) y 100 a fondo (pinza cerrada, 0°). Las líneas que empiezan con `#` son mensajes de estado y el visualizador y Unity las ignoran.

## Conexiones

| Señal | ESP32 |
|---|---|
| SDA | GPIO 21 |
| SCL | GPIO 22 |
| Joystick derecho (VRx o VRy) | GPIO 34 |
| Joystick izquierdo (VRx o VRy) | GPIO 35 |

Los joysticks y los pull-up del I2C van a 3.3 V. Las entradas analógicas de la ESP32 no toleran 5 V.

Las direcciones de los multiplexores se definen en el arreglo `manos[]` del código.

## Filtrado y calibración

Solo el eje 2 de cada mano lleva filtro: mediana circular de 5 lecturas y luego un suavizado exponencial (alfa 0.25). Los demás ejes salen sin filtrar para no meter retardo.

Al arrancar, el firmware toma 100 lecturas de cada joystick en reposo para fijar el centro y calcular la zona muerta a partir del ruido medido. Por eso no hay que tocar los joysticks durante el primer segundo. Después el centro se sigue corrigiendo despacio mientras el joystick está suelto, y el 100 % se aprende con el mayor recorrido que se haya visto.

Si en una vuelta no responde ningún encoder, el bus I2C se reinicia solo.

## Compilar

Arduino IDE con el paquete de placas de ESP32 de Espressif, placa "ESP32 Dev Module". No necesita librerías externas, solo `Wire.h`.
