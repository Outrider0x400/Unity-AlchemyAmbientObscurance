Alchemy Ambient Obscurance for Unity 3D
------------
![screenshot](sponza_raw.png)
![screenshot](sibenik_raw.png)
This is a lightweight script & shader that implements the Alchemy Ambient Obscurance for Unity 3D, with some added techniques from HBAO+ and Separable AO.

Deferred shading path & HDR is required. 

Simply attach the script to the main camera of the scene, and attach the compute shader to the script. (I'll update it so this will be done automatically)

On my 940MX, the rendering of these two images took less than 8ms, at 1080p.

References:

[Alchemy AO](https://research.nvidia.com/publication/alchemy-screen-space-ambient-obscurance-algorithm)

[Deinterleaving Depth Buffer](https://developer.nvidia.com/sites/default/files/akamai/gamedev/docs/BAVOIL_ParticleShadowsAndCacheEfficientPost.pdf)

[Two-pass Separable Appoarch](https://perso.telecom-paristech.fr/boubek/papers/SAO/)