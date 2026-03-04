# unity-web-api-service
A web API service for Unity that leverages modern dev techniques

### Installation

The minimum Unity support for R3 is **Unity 2021.3**.

There are two installation steps required to use it in Unity.

1. Install `R3` from NuGet using [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)

* Open Window from NuGet -> Manage NuGet Packages, Search "R3" and Press Install.
![](https://github.com/Cysharp/ZLogger/assets/46207/dbad9bf7-28e3-4856-b0a8-0ff8a2a01d67)

* If you encounter version conflict errors, please disable version validation in Player Settings(Edit -> Project Settings -> Player -> Scroll down and expand "Other Settings" than uncheck "Assembly Version Validation" under the "Configuration" section).

2. Install the `R3.Unity` package by referencing the git URL

```
https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity
```

![image](https://github.com/Cysharp/ZLogger/assets/46207/7325d266-05b4-47c9-b06a-a67a40368dd2)
![image](https://github.com/Cysharp/ZLogger/assets/46207/29bf5636-4d6a-4e75-a3d8-3f8408bd8c51)

R3 uses the *.*.* release tag, so you can specify a version like #1.0.0. For example: `https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#1.0.0`

3. Install the `UniTask` package by referencing the git URL

```
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.10
```

Again, you can change the release tag as needed.

4. Install this package by referencing the git URL

```
 https://github.com/SplenSoft/unity-web-api-service.git?path=/Packages/com.splensoft.webapiservice
```
