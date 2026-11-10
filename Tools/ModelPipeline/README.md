# Tractor model pipeline

The game does not import the FBX through Unity. `fbx2trm.py` converts it offline into
`Assets/Resources/Art/TractorModel.bytes`, which `TractorModel.cs` loads at runtime.

The converter:
- applies the FBX transforms and converts to Unity's left-handed metres;
- splits the model into body, four wheels, trolley chassis, tipping bed and trolley wheels;
- welds vertices;
- stores the measurements that the physics uses.

    python3 fbx2trm.py tractor.fbx ../../Assets/Resources/Art/TractorModel.bytes
    python3 trmview.py ../../Assets/Resources/Art/TractorModel.bytes preview.png groups 35   # check the part split

Textures (`Assets/Resources/Art/TractorTex`) were unpacked from the BlenderKit .blend:
- base colour and OpenGL normal maps are used as they are;
- metallic + (1 − roughness) are packed into `MetalSmooth_*.png`;
- the glass tile (1003) gets its opacity as alpha.
