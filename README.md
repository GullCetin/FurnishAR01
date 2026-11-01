# Furnish AR

Furnish AR is a Unity 6 augmented-reality furniture placement application for Android. It uses AR Foundation and ARCore to detect horizontal surfaces, place furniture models, and let users move, rotate, select, and remove items in a room.

## Technology

- Unity `6000.6.7f1`
- AR Foundation / ARCore `6.6.2`
- Android ARM64, IL2CPP
- 15 furniture prefabs from the included NextGen Furniture Pack

## Getting started

1. Open the project with Unity `6000.6.7f1`.
2. Open `Assets/FurnishAR/Scenes/FurnishARScene.unity`.
3. Install the Android platform module and build for an ARCore-compatible Android device.
4. Grant camera permission, scan a horizontal surface, then choose a furniture item from the catalog.

For the full Turkish usage guide and validation notes, see [README-FurnishAR.md](README-FurnishAR.md).

## Main components

- [ARPlacementManager](Assets/FurnishAR/Scripts/Core/ARPlacementManager.cs) — surface detection and placement.
- [PlacedFurniture](Assets/FurnishAR/Scripts/Core/PlacedFurniture.cs) — furniture and anchor lifecycle.
- [FurnitureInteractionManager](Assets/FurnishAR/Scripts/Core/FurnitureInteractionManager.cs) — selection, movement, rotation, and deletion.
- [AppRuntimeUI](Assets/FurnishAR/Scripts/UI/AppRuntimeUI.cs) — runtime catalog and controls.
- [FurnitureCatalog](Assets/FurnishAR/Resources/FurnitureCatalog.asset) — catalog data.

## Validation

The Unity editor menu provides model-dimension validation and Play Mode checks for all 15 catalog items. Generated audit files are kept under `Builds/` locally and are intentionally excluded from version control.

Physical camera tracking, anchor relocalization, device performance, and permission flow still require testing on an ARCore-compatible Android phone.

## License

This repository is private. The included furniture assets remain subject to their original license and are not redistributed separately from this project.
