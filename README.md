# Object Alignment

Object Alignment is a sample application that demonstrates how to attach large scale 3D content, such as digital twin meshes, to a spatial localization map such that the 3D content remains stuck to the world as the user moves around a large space.

This sample includes the ability to load arbitrary glb models in addition to having a few built-in sample models. Once instantiated, a 3D model can be moved and rotated using the Controller 6DoF ray + trigger-select. For precise alignment, a second pinning tool can be used to attach distinctive architectural and/or texture points to the physical world using pins. Any number of pins are supported. For large scale 3D objects such as digital twin meshes, it is recommended that pin attachment points be spread out across the entire object. Object interpolation is available and recommended in order to prioritize the object position based on the user's position.

## Usage Instructions

1. Open the Spaces app on your device and create a Local or Shared space.
2. Install and open the Object Alignment app on your device. This app code can be integrated into a Unity project in order to utilize this tool for more seamless object alignment.
3. Tap the Menu button on your Controller to toggle and move the main menu.
4. Refer to the additional in-app instructions.

### Adding custom 3D models

* To add 3D models to the app for viewing:
  * `adb push /path/to/glft/or/glb/file /storage/emulated/0/Android/data/com.magicleap.objectalignment/files`

## Copyright

Copyright (c) 2023-present Magic Leap, Inc. All Rights Reserved.
Use of this file is governed by the Developer Agreement, located
here: https://www.magicleap.com/software-license-agreement-ml2
