これはStarsInterface.dllのフレームワークを.NET Standard2.0に変更し、一部機能の変更を行ったものです。
StarsInterface.dllと互換性のある部分とない部分があります。

アセンブリ名：StarsInterface_dotNetStandard.dll
名前空間：STARS

クラス一覧
StarsInterface / 本体です
StarsMessage / STARSの受信メッセージ型
StarsCbArgs : EventArgs / コールバックでのSTARS受信メッセージ型
StarsException : ApplicationException / 例外メッセージ
StarsConvParams / SATRSパラメータの変換メソッド等
(internal)StateObject / ソケット受信パラメータ

StarsInterfaceのプロパティ
string NodeName / ノード名
string ServerHostname / STARSサーバー名
int ServerPort / ポート名
string KeyFile / キーファイルパス
string KeyWord / キーワード（KeWordから名称変更）
decimal DefaultTimeout / タイムアウト時間：秒（新規項目、デフォルト30秒）
bool IsConnected / STARSサーバへの接続状態（新規項目、リードオンリー）

StarsInterfaceのコンストラクタ（従来互換性あり）
public StarsInterface(string nodeName, string svrHost, string keyFile, int svrPort, decimal timeOut = 30.0m)
public StarsInterface(string nodeName, string svrHost, string keyFile)
public StarsInterface(string nodeName, string svrHost)
timeOut項だけオプション追加
KeyWord使用時はインスタンス生成後に設定

StarsInterfaceのメソッド
void Connect(bool callbackmode = false) / オプションで接続と同時にコールバックスタートを追加
void Disconnect() / 変更無し
bool CallbackOn() / （新規追加）コールバックをスタートする際に使用 Connectでスタートした場合は実行不要
void Send(string sndFrom, string sndTo, string sndCommand) / 変更無し
void Send(string sndTo, string sndCommand) / 変更無し
void Send(string sndCommand) / 変更無し
StarsMessage Receive() / 変更無し
StarsMessage Receive(int timeout) / 変更無し

StarsInterfaceのイベント
event EventHandler<StarsCbArgs> DataReceived
（新規追加）以前のイベントシステムは廃止

2019/11/19 Hiroaki NITANI