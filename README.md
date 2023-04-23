# Valheim - Building Health Mode
This mod will **visually** display the **health** of building peices around your player (in the style of a **heatmap**). 
The Building Health Mode can be toggled on and off using a configurable keybind (**H** by default) or enabled automatically whenever you are using the repair mode on the hammer (enabled by default, configurable).

# Installation
You must have BenInEx installed.\
Copy **BuildingHealthMode.dll** into **Valheim\BepInEx\plugins**\

# Config
There are various configurations for this mod. For example. you can specify the keybind to enable the mod and the colour range for zero health and max
health. Due to the somewhat intensive nature of this mod, some users may experience performance issues while using it, for this reason I've included quite a few
optimisation config options. If you are experience low FPS, I encourage you to experiment with these options and potentially lower some of the values, such as Max Distance.

# Screenshot
![alt text](https://i.imgur.com/SUVFr2g.png "Screenshot")

# Changelog
    - Version 1.2.0 -
        Added option to control whether full health objects are highlighted (disabled by default because that's why I made this fork)
        Improved color interpolation, half dead objects are now bright yellow instead of brown.
    - Version 1.1.2 -
        Fixed major bug - mode flickers when playing with others
    - Version 1.1.1 -
        Fixed bug where if auto mode was enabled, the player could not manually disable the mod.
    - Version 1.1.0
        Added option to enable automatically when hammer is on repair mode (enabled by default).
        Force peices to highlight blue when mod is enabled. Fixed old materials being reset every frame.
        Fixed old materials being reset every frame.
    - Version 1.0.0 -
        Initial Release. No bugs known.

