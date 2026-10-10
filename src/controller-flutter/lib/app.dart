import 'package:flutter/material.dart';
import 'state/agent_controller.dart';
import 'ui/home_shell.dart';

class PulseCheckApp extends StatefulWidget {
  const PulseCheckApp({super.key});

  @override
  State<PulseCheckApp> createState() => _PulseCheckAppState();
}

class _PulseCheckAppState extends State<PulseCheckApp> {
  late final AgentController controller;

  @override
  void initState() {
    super.initState();
    controller = AgentController();
    controller.restore();
  }

  @override
  void dispose() {
    controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: controller,
      builder: (context, _) => MaterialApp(
        title: 'PulseCheck',
        debugShowCheckedModeBanner: false,
        themeMode: controller.themeMode,
        theme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF0D6EFD)), useMaterial3: true),
        darkTheme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF4DA3FF), brightness: Brightness.dark), useMaterial3: true),
        home: HomeShell(controller: controller),
      ),
    );
  }
}