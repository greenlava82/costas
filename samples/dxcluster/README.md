# DX cluster samples

`spots.txt` is what `--simulate` replays as DX cluster spots (`DxClusterService`, docs/05-integrations.md 5b). The app
moves the times so the newest spot is a minute old, keeping their spacing. Nothing connects to a real node in simulation.

- The `P3AA`, `OZ8BLR` and `UX1MW` lines are as received from a DXSpider node (N6WS-6), and the `TR8CA` line from a
  skimmer feed, quoted in the SparkSDR group thread "SparkSDR meets DXCluster"
  (https://groups.google.com/g/sparksdr/c/SgclcMcthaY). The `DK7JI` line is RBN output quoted on the DXLog.net list
  (https://dxlog.net/archive/2017-April/001678.html). Their times were changed.
- The other lines are written in the same layout for calls heard in the `samples/ft8` recordings (so the Operate screen
  shows the Spotted line under Why) and on other bands (so the Awards view lists needed stations). They are examples,
  not captures.
