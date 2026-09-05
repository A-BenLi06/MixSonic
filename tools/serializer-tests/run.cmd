@echo off
setlocal
rem Desktop regression suite for the Subsonic8 XML serialization surface.
rem Compiles the model/result sources for the desktop CLR (no WinRT needed),
rem asserts every model type constructs an XmlSerializer, and round-trips the
rem previously-crashing Genre and InternetRadioStation shapes against
rem Navidrome-style XML. Exit code 0 = all tests passed.
cd /d "%~dp0..\.."
set TOOLSDIR=%~dp0
set CALIBURN=
for /f "delims=" %%f in ('dir /s /b packages\Caliburn.Micro.Core.3.2.0\lib\Caliburn.Micro.dll 2^>nul') do set CALIBURN=%%f
if "%CALIBURN%" == "" ( echo Caliburn.Micro.Core not found in packages/ & exit /b 1 )
if not exist "%TOOLSDIR%Caliburn.Micro.dll" copy /y "%CALIBURN%" "%TOOLSDIR%Caliburn.Micro.dll" >nul
"C:\Program Files (x86)\MSBuild\14.0\Bin\csc.exe" -nologo -main:SerializerTests -out:"%TOOLSDIR%runtests.exe" @tools\serializer-tests\sources.txt tools\serializer-tests\VisualStateResultStub.cs tools\serializer-tests\SerializerTests.cs -r:"%TOOLSDIR%Caliburn.Micro.dll" -r:System.Net.Http.dll -r:System.ServiceModel.dll -r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.Serialization.dll" -r:System.Xml.dll -r:System.Xml.Linq.dll -r:Microsoft.CSharp.dll
if errorlevel 1 exit /b 1
"%TOOLSDIR%runtests.exe"
