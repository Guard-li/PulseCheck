import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:web_socket_channel/io.dart';
import '../models/agent_models.dart';
import '../services/agent_api.dart';
import '../services/discovery_service.dart';

class AgentController extends ChangeNotifier {
  final FlutterSecureStorage secureStorage = const FlutterSecureStorage();
  final DiscoveryService discoveryService = DiscoveryService();
  final List<MetricSnapshot> snapshots = [];
  AgentConnection? connection;
  AgentApi? api;
  MetricSnapshot? latest;
  List<ReportSummary> reports = [];
  List<ScanRecord> scans = [];
  List<DiscoveredAgent> discoveredAgents = [];
  String? error;
  bool loading = false;
  ThemeMode themeMode = ThemeMode.dark;
  int selectedIndex = 0;
  IOWebSocketChannel? _channel;
  StreamSubscription? _socketSubscription;

  Future<void> restore() async {
    final preferences = await SharedPreferences.getInstance();
    themeMode = preferences.getString('theme') == 'light' ? ThemeMode.light : ThemeMode.dark;
    final base = await secureStorage.read(key: 'baseUrl');
    final token = await secureStorage.read(key: 'token');
    final name = await secureStorage.read(key: 'deviceName') ?? 'PulseCheck Agent';
    final fingerprint = await secureStorage.read(key: 'fingerprint');
    if (base != null && token != null) {
      connection = AgentConnection(baseUrl: base, token: token, deviceName: name, fingerprint: fingerprint);
      await refresh();
    }
    notifyListeners();
  }

  Future<void> pair({required String baseUrl, required String code, String? fingerprint}) async {
    loading = true;
    error = null;
    notifyListeners();
    try {
      final result = await AgentApi(AgentConnection(baseUrl: baseUrl, token: '', deviceName: '', fingerprint: fingerprint)).pair(baseUrl, code, fingerprint: fingerprint);
      connection = result;
      await secureStorage.write(key: 'baseUrl', value: result.baseUrl);
      await secureStorage.write(key: 'token', value: result.token);
      await secureStorage.write(key: 'deviceName', value: result.deviceName);
      if (fingerprint != null) await secureStorage.write(key: 'fingerprint', value: fingerprint);
      await refresh();
    } catch (exception) {
      error = exception.toString();
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> refresh() async {
    final active = connection;
    if (active == null || active.token.isEmpty) return;
    try {
      api = AgentApi(active);
      latest = await api!.current();
      snapshots
        ..clear()
        ..addAll(await api!.history(limit: 2000));
      reports = await api!.reports();
      error = null;
      await _connectRealtime(active);
    } catch (exception) {
      error = exception.toString();
    }
    notifyListeners();
  }

  Future<void> _connectRealtime(AgentConnection active) async {
    if (_socketSubscription != null) await _socketSubscription!.cancel();
    if (_channel != null) await _channel!.sink.close();
    final uri = Uri.parse('${active.baseUrl.replaceFirst(RegExp(r'^http'), 'ws')}/api/v1/realtime');
    final socket = await WebSocket.connect(uri.toString(), protocols: ['pulsecheck.v1', 'token.${active.token}']);
    _channel = IOWebSocketChannel(socket);
    _socketSubscription = _channel!.stream.listen((event) {
      final envelope = jsonDecode(event as String) as Map<String, dynamic>;
      if (envelope['type'] == 'metrics') {
        final snapshot = MetricSnapshot.fromJson(envelope['payload'] as Map<String, dynamic>);
        latest = snapshot;
        snapshots.add(snapshot);
        if (snapshots.length > 5000) snapshots.removeRange(0, snapshots.length - 5000);
        notifyListeners();
      }
    }, onError: (Object exception) { error = exception.toString(); notifyListeners(); }, onDone: () {});
  }

  Future<void> discover() async {
    discoveredAgents = await discoveryService.discover();
    notifyListeners();
  }

  Future<ScanRecord?> startScan({bool full = false, bool generatePdf = true}) async {
    if (api == null) return null;
    loading = true; notifyListeners();
    try {
      final scan = await api!.startScan(full: full, generatePdf: generatePdf);
      scans.insert(0, scan);
      return scan;
    } catch (exception) {
      error = exception.toString();
      return null;
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> refreshScan(ScanRecord scan) async {
    if (api == null) return;
    try {
      final updated = await api!.scan(scan.id);
      final index = scans.indexWhere((item) => item.id == updated.id);
      if (index >= 0) scans[index] = updated;
      notifyListeners();
    } catch (_) {}
  }

  void changeTheme() {
    themeMode = themeMode == ThemeMode.dark ? ThemeMode.light : ThemeMode.dark;
    SharedPreferences.getInstance().then((preferences) => preferences.setString('theme', themeMode == ThemeMode.dark ? 'dark' : 'light'));
    notifyListeners();
  }

  void selectTab(int index) { selectedIndex = index; notifyListeners(); }

  @override
  void dispose() {
    _socketSubscription?.cancel();
    _channel?.sink.close();
    api?.close();
    super.dispose();
  }
}