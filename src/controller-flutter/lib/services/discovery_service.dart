import 'dart:async';
import 'dart:convert';
import 'dart:io';

class DiscoveredAgent {
  const DiscoveredAgent({required this.deviceId, required this.displayName, required this.host, required this.port, required this.fingerprint});
  final String deviceId;
  final String displayName;
  final String host;
  final int port;
  final String fingerprint;
}

class DiscoveryService {
  Future<List<DiscoveredAgent>> discover({Duration timeout = const Duration(seconds: 4)}) async {
    final socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);
    socket.broadcastEnabled = true;
    final result = <String, DiscoveredAgent>{};
    final completer = Completer<void>();
    socket.listen((event) {
      if (event != RawSocketEvent.read) return;
      final datagram = socket.receive();
      if (datagram == null) return;
      try {
        final json = jsonDecode(utf8.decode(datagram.data)) as Map<String, dynamic>;
        if (json['service'] != 'PulseCheck') return;
        final host = datagram.address.address;
        final agent = DiscoveredAgent(
          deviceId: json['deviceId'] as String,
          displayName: json['displayName'] as String? ?? 'PulseCheck Agent',
          host: host,
          port: (json['apiPort'] as num).toInt(),
          fingerprint: json['certificateSha256'] as String? ?? '',
        );
        result['${agent.deviceId}|${agent.host}'] = agent;
      } catch (_) {}
    });
    socket.send(utf8.encode('PULSECHECK_DISCOVER_V1'), InternetAddress('255.255.255.255'), 17820);
    await Future.any([Future<void>.delayed(timeout), completer.future]);
    socket.close();
    return result.values.toList();
  }
}