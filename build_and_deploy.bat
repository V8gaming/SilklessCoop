@echo off
echo Building SilklessCoop...
dotnet build

if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)

echo Build successful! Copying to BepInEx plugins folder...

copy "C:\Users\User\Desktop\Projects\SilklessCoop\bin\Debug\netstandard2.1\SilklessCoop.dll" "C:\SteamLibrary\steamapps\common\Hollow Knight Silksong\BepInEx\plugins\"

if %ERRORLEVEL% NEQ 0 (
    echo Copy failed!
    pause
    exit /b 1
)

echo Deploy successful!
pause