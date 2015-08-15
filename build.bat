@echo off
setlocal
call "C:\Program Files (x86)\Microsoft Visual Studio 12.0\vc\vcvarsall.bat" x86
set GITROCKET_ZIP=git-rocket-filter-v1.0.zip
set GITROCKET_RELEASE_FOLDER=%~dp0\Bin\Release
RMDIR /S /Q "%GITROCKET_RELEASE_FOLDER%"
msbuild /tv:4.0 /t:Build /verbosity:quiet /clp:ErrorsOnly /fl /flp:logfile=BuildErrors.log;ErrorsOnly "/p:Configuration=Release;Platform=Any CPU" GitRocketFilter.sln
if %ERRORLEVEL% NEQ 0 GOTO :EXIT_ERROR
XCOPY /Y /D Readme.md "%GITROCKET_RELEASE_FOLDER%"
XCOPY /Y /D License.txt "%GITROCKET_RELEASE_FOLDER%"
DEL  "%GITROCKET_RELEASE_FOLDER%\*.xml" "%GITROCKET_RELEASE_FOLDER%\*.pdb"
PUSHD "%GITROCKET_RELEASE_FOLDER%"
DEL "%~dp0\%GITROCKET_ZIP%"
"%~dp0\external\7-Zip\7z.exe" a "%~dp0\%GITROCKET_ZIP%" .
POPD
goto :EOF
:EXIT_ERROR
echo "Unexpected errors while building git-rocket-filter"


