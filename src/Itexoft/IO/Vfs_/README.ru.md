# VirtualFileSystem

## Назначение

`VirtualFileSystem` реализует `IFileSystem` поверх одного или двух carrier-стримов `IStreamRwsl<byte>`.
Carrier трактуется как абстрактный persistent image. Ядро VFS не использует BCL `Stream` как часть своего канонического
протокола.

## Публичный контракт

- Один carrier:
    - `VirtualFileSystem(IStreamRwsl<byte> image, in VirtualFileSystemOptions options = default)`
- Зеркальная пара:
    -
    `VirtualFileSystem(IStreamRwsl<byte> primary, IStreamRwsl<byte> mirror, in VirtualFileSystemOptions options = default)`
- `VirtualFileSystemOptions`:
    - `ChunkSize`
    - `BufferSize`

`ChunkSize` означает только число payload-байтов в одном data chunk.
`BufferSize` задаёт верхнюю границу внешнего рабочего окна, но не меняет закон исполнения. Корректность обязана
сохраняться при `BufferSize = 1`.

`VirtualFileSystem` не владеет входными carrier-стримами и не закрывает их при `Dispose()`.

## Формат образа

Каноническое состояние задаётся так:

1. `bootstrap prefix`
2. immutable persistent maps
3. data chunks

`bootstrap prefix` двухслотовый. Каждый slot имеет фиксированный размер `76` байт и хранит:

- `Int32 Version`
- `Int32 ChunkSize`
- `Int64 Generation`
- `Int64 NamespaceRoot`
- `Int64 InodeRoot`
- `Int64 AttributeRoot`
- `Int64 ReuseRoot`
- `Int64 DeferredReuseRoot`
- `Int64 RootDirectoryInodeId`
- `Int64 LastInodeId`
- `UInt32 SlotChecksum`

В prefix нет magic-сигнатур. Валидность slot-а определяется только структурой, диапазонами полей и `SlotChecksum`.
Активным считается валидный slot с максимальным `Generation`. Если оба slot-а имеют одинаковый `Generation`, но разные
root-descriptor-ы, mount завершается ошибкой.

После prefix идёт chunk-area. Каждый chunk record имеет фиксированный физический размер `ChunkSize + 24` и содержит:

- `Byte Kind`
- `Byte Flags`
- `UInt16 Reserved`
- `Int64 LinkChunkId`
- `Int32 UsedBytes`
- `UInt32 PayloadChecksum`
- `UInt32 HeaderChecksum`
- payload длиной ровно `ChunkSize`

`ChunkSize` относится только к payload. Заголовок chunk-а не входит в `ChunkSize`.
В chunk header также нет magic-сигнатур. Валидность определяется только структурой, диапазонами полей, `HeaderChecksum`
и `PayloadChecksum`.

## Snapshot descriptor

Текущий snapshot задаётся целиком через активный slot prefix-а. Отдельного root-record chunk-а нет.

In-memory опубликованный snapshot идентифицируется только `Generation` и держится через `EpochPublished64<long>`.

Snapshot descriptor содержит:

- `NamespaceRoot`
- `InodeRoot`
- `AttributeRoot`
- `ReuseRoot`
- `DeferredReuseRoot`
- `RootDirectoryInodeId`
- `LastInodeId`

## Канонические карты

Все карты используют одну общую immutable map-grammar.

- `namespace map`
    - key: нормализованный path
    - value: `(inodeId, kind)`
- `inode map`
    - key: `inodeId`
    - value: `(length, contentRoot, fileAttributes)`
- `attribute map`
    - key: `(inodeId, attributeName)`
    - value: `byte[]`
- `content map`
    - key: `logicalChunk`
    - value: `physicalDataChunkId`
- `reuse map`
    - key: `physicalChunkId`
    - value: пусто
- `deferred reuse log`
    - metadata chunk stream со списком retired chunk id

Map node хранится как metadata node-stream и может занимать один или несколько metadata chunk-ов.

Node header содержит:

- `node kind`
- `level`
- `record count`
- `encoded byte length`

Leaf record:

- `keyLength`
- `keyBytes`
- `valueLength`
- `valueBytes`

Internal record:

- `separatorKeyLength`
- `separatorKeyBytes`
- `childRootId`

Ключи сравниваются лексикографически по каноническим байтам.

## Закон исполнения

Чтение:

1. reader входит в `Epoch64`
2. читает опубликованный `Generation`
3. читает соответствующий slot prefix-а
4. разрешает path через `namespace map`
5. разрешает inode через `inode map`
6. разрешает content chunks через `content map`
7. передаёт payload пользователю

Один вызов чтения работает только внутри одного snapshot-а.

Запись:

1. мутация сериализуется через lane-slot combiner
2. строится один mutation-plan и один replayable delta
3. новые data chunk-ы пишутся copy-on-write
4. переписываются только затронутые map path-ы
5. предыдущий `DeferredReuseRoot` потоково промотируется в `ReuseRoot`
6. retired chunk-ы текущей мутации попадают в новый `DeferredReuseRoot`
7. публикуется новый slot prefix-а
8. переключается `Epoch64`

Нулевые логические области не материализуются. Отсутствие записи в `content map` внутри `Length` означает чтение нулей.

Отдельного `RunCompaction()` нет. Фонового обслуживания нет.

## Reuse / retire

Chunk, ставший недостижимым в текущей мутации, не может быть возвращён в reuse в той же мутации.

Текущая ревизия держит один канонический обычный путь:

- в начале мутации предыдущий `DeferredReuseRoot` промотируется в `ReuseRoot`
- ordinary allocator сначала детерминированно берёт id из `ReuseRoot`
- если `ReuseRoot` пуст, allocator падает в append-only хвост
- retired chunk-ы текущей мутации записываются в новый `DeferredReuseRoot`
- rewrite самого `ReuseRoot` идёт в append-only режиме, чтобы free-space law не зацикливался на самом себе
- ordinary read/write/delete не сканируют всю историю

`ReuseRoot` снова является обычным каноническим представлением свободного пространства, а `DeferredReuseRoot` хранит
только pending retire-batch одной последней опубликованной мутации.

## Mirror-протокол

Mirror ориентирован на crash-model, где carrier, в который сейчас идёт запись, может после потери питания стать
полностью нулевым или произвольно повреждённым.

Поэтому обычная мутация публикуется строго так:

1. на `secondary` вычисляется один mutation-plan и публикуется один snapshot
2. `secondary` flush
3. из `secondary` читается реально опубликованный slot prefix
4. exact replay того же delta выполняется в `primary`
5. `primary` flush
6. из `primary` читается реально опубликованный slot prefix
7. сравниваются `Generation`, `ChunkSize` и весь snapshot descriptor

Если опубликованные prefix-снимки расходятся, мутация считается аварийной и завершается ошибкой.

При mount:

- если валиден один carrier, он становится каноном
- если валидны оба, побеждает больший `Generation`
- если поколения равны и snapshot descriptor расходится, mount завершается ошибкой
- проигравший carrier догоняется только во время mount-recovery

Полное копирование reachable snapshot допустимо только в mount-recovery для починки проигравшего carrier-а. На обычную
мутацию full-copy запрещён.

При mount carrier также жёстко подрезается до reachable-tail выбранного snapshot-а, поэтому orphan chunk-ы после аварии
не становятся второй онтологией.

## Синхронизация

- логические мутации сериализуются через `LaneIdPool64` + `InlineLaneSlotCombiner64`
- опубликованный snapshot держится через `EpochPublished64<long>`
- чтение/запись carrier-а локализованы в positional I/O слое поверх `IStreamRwsl<byte>`
- `AtomicLock` допускается только внутри positional I/O слоя как защита shared `Position`
- `lock`, `ReaderWriterLockSlim`, `Promise`, фоновые задачи и таймеры в ядре VFS отсутствуют
