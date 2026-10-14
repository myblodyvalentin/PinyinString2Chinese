# 拼音还原

框住一段已经变成英文的字符串，软件会删掉原文，并把里面的字母和数字全部送进系统里指定的中文输入法。缩写也可以，不要求是标准拼音。候选词留在输入法里，不会自动按空格或回车确认。

适合这种情形：拼音打完后不小心按了回车，屏幕上只剩 `nihao`、`btw`、`yyds`、`u1s1` 这一类英文。

## 安装

发布页：https://github.com/myblodyvalentin/PinyinString2Chinese/releases/tag/v1.0.0

下载 `PinyinRestore-Setup-1.0.0.exe`。安装程序是简体中文，装到当前用户目录，不需要管理员权限。装完后从开始菜单打开「拼音还原」，也可以勾选桌面快捷方式。

这台电脑需要已经安装 [.NET 9 桌面运行时](https://dotnet.microsoft.com/download/dotnet/9.0)。

v1.0.0 安装包不包含后来的记事本修正。要使用下面「使用」里关于记事本选区和菜单栏的行为，请从当前源码编译运行。

## 使用

启动后会出现托盘图标和桌面右下角的悬浮球。绿色「开」表示正在监听框选，灰色「关」表示已暂停。

- 单击悬浮球：开启或暂停自动还原。按住可拖动，右键打开托盘菜单。双击托盘图标同样可以开关。
- 用鼠标框住一段英文，或双击选中它。原文会被删掉，字符会进入指定的中文输入法。框选会等文字稳定后再读取，避免第一下没读到。
- Windows 11 记事本按编辑框自己的文字位置读取，换行不会把选区错位或截断。框选结束不会去点菜单栏，字母不会变成「查看」这类快捷键。如果菜单栏已经亮着，先按一次 `Esc` 再框选。
- 暂停：`Ctrl+Alt+P`。如果这个组合被占用，会改用 `Ctrl+Alt+F8`。
- 手动还原当前选中：`Ctrl+Alt+R`。如果被占用，会改用 `Ctrl+Alt+F9`。暂停期间仍可用这个快捷键还原。
- 送错了：先按 `Esc` 取消输入法里的内容，再按 `Ctrl+Z`，原文会回到原处。

右键托盘图标可以打开设置。可以改成「按住 Alt 再框选才还原」，也可以选择中文输入法，以及登录 Windows 后自动运行。

字符串里的空格不会打进输入法，避免中途确认候选。超过 120 个字符、带标点、带中文、带换行的内容不会处理。资源管理器和密码框也不会处理。

设置和日志在 `%AppData%\PinyinString2Chinese\`。

## 从源码编译

需要 .NET 9 SDK。

```powershell
dotnet run --project src\PinyinString2Chinese\PinyinString2Chinese.csproj
dotnet run --project tests\PinyinParserTests\PinyinParserTests.csproj -c Debug --nologo
```

打安装包还需要 [Inno Setup 6](https://jrsoftware.org/isdl.php)。先发布，再编译安装脚本，安装包会出现在 `dist\`：

```powershell
dotnet publish src\PinyinString2Chinese\PinyinString2Chinese.csproj -c Release -o artifacts\publish
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
```
